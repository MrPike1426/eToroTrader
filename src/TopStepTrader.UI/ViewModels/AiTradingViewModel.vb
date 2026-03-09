Imports System.Collections.ObjectModel
Imports System.Threading
Imports System.Windows
Imports System.Windows.Media
Imports Microsoft.Extensions.Options
Imports TopStepTrader.API.Http
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.Core.Trading
Imports TopStepTrader.Services.AI
Imports TopStepTrader.Services.Trading
Imports TopStepTrader.UI.ViewModels.Base

Namespace TopStepTrader.UI.ViewModels

    ''' <summary>
    ''' ViewModel for the AI-Assisted Trading tab.
    ''' Loads accounts and contracts from the API, parses natural-language strategies,
    ''' calls Claude for AI review, and starts/stops the StrategyExecutionEngine.
    ''' </summary>
    Public Class AiTradingViewModel
        Inherits ViewModelBase
        Implements IDisposable

        ' ── Dependencies ──────────────────────────────────────────────────────────
        Private ReadOnly _accountService As IAccountService
        Private ReadOnly _contractClient As ContractClient
        Private ReadOnly _parserService As StrategyParserService
        Private ReadOnly _reviewService As ClaudeReviewService
        Private ReadOnly _engine As StrategyExecutionEngine
        Private ReadOnly _tradingSettings As TradingSettings

        ' ── Internal state ────────────────────────────────────────────────────────
        Private _currentStrategy As StrategyDefinition
        Private _disposed As Boolean = False
        Private _searchCts As CancellationTokenSource
        Private _favoriteContracts As New List(Of ContractDto)

        ' ── Accounts ──────────────────────────────────────────────────────────────
        Public Property Accounts As New ObservableCollection(Of Account)

        Private _selectedAccount As Account
        Public Property SelectedAccount As Account
            Get
                Return _selectedAccount
            End Get
            Set(value As Account)
                SetProperty(_selectedAccount, value)
                NotifyPropertyChanged(NameOf(IsFormReady))
            End Set
        End Property

        ''' <summary>
        ''' Called by MainWindow when the user navigates from Dashboard → AI Trade.
        ''' Pre-selects the account they already chose on Dashboard so they don't
        ''' have to pick it a second time.
        ''' </summary>
        Public Sub SyncDashboardAccount(account As Account)
            If account Is Nothing Then Return
            ' Add to collection if AI Trading hasn't loaded accounts yet.
            If Not Accounts.Any(Function(a) a.Id = account.Id) Then
                Accounts.Insert(0, account)
            End If
            ' Select the matching entry (prefer already-loaded instance over dashboard copy).
            Dim match = Accounts.FirstOrDefault(Function(a) a.Id = account.Id)
            SelectedAccount = If(match IsNot Nothing, match, account)
        End Sub

        ' ── Contract search ───────────────────────────────────────────────────────
        ''' <summary>Bound to ContractSelectorControl.AvailableContracts — favourite contracts only.</summary>
        Public Property AvailableContracts As New ObservableCollection(Of Contract)()

        Public Property FilteredContracts As New ObservableCollection(Of ContractDto)

        Private _isContractDropDownOpen As Boolean = False
        Public Property IsContractDropDownOpen As Boolean
            Get
                Return _isContractDropDownOpen
            End Get
            Set(value As Boolean)
                ' When the user opens the dropdown with an empty search box, show favourites.
                If SetProperty(_isContractDropDownOpen, value) AndAlso value AndAlso
                   String.IsNullOrEmpty(_contractSearchText) Then
                    FilteredContracts.Clear()
                    For Each fav In _favoriteContracts
                        FilteredContracts.Add(fav)
                    Next
                End If
            End Set
        End Property

        Private _contractSearchText As String = String.Empty
        Public Property ContractSearchText As String
            Get
                Return _contractSearchText
            End Get
            Set(value As String)
                If SetProperty(_contractSearchText, value) Then
                    ' When the user picks from the dropdown, WPF sets Text to the item's
                    ' DisplayLabel.  Don't re-search in that case — it would fire an extra
                    ' API call and could clear FilteredContracts before ContractId is saved.
                    If _selectedContract IsNot Nothing AndAlso
                       String.Equals(value, _selectedContract.DisplayLabel,
                                     StringComparison.OrdinalIgnoreCase) Then
                        Return
                    End If
                    ' When the field is cleared, restore favourites without an API call.
                    If String.IsNullOrEmpty(value) Then
                        FilteredContracts.Clear()
                        For Each fav In _favoriteContracts
                            FilteredContracts.Add(fav)
                        Next
                        Return
                    End If
                    ' Cancel any in-flight search and start a fresh debounced one.
                    ' CTS management happens here on the UI thread so there is no race.
                    _searchCts?.Cancel()
                    _searchCts?.Dispose()
                    _searchCts = New CancellationTokenSource()
                    Dim token = _searchCts.Token
                    Task.Run(Async Function()
                                 Await SearchContractsAsync(value, token)
                             End Function)
                End If
            End Set
        End Property

        Private _selectedContract As ContractDto
        Public Property SelectedContract As ContractDto
            Get
                Return _selectedContract
            End Get
            Set(value As ContractDto)
                If SetProperty(_selectedContract, value) AndAlso value IsNot Nothing Then
                    IsContractDropDownOpen = False
                    ' Auto-fill the Contract ID box from the ticker symbol
                    Dim id = If(Not String.IsNullOrWhiteSpace(value.ContractId),
                                value.ContractId,
                                value.InstrumentId.ToString())
                    SelectedContractId = id
                End If
            End Set
        End Property

        ''' <summary>
        ''' Editable contract code string (e.g. "CON.F.US.MBT.G26").
        ''' Auto-filled when a contract is selected from the dropdown; can also be typed directly.
        ''' This is what gets passed to the execution engine as ContractId.
        ''' </summary>
        Private _selectedContractId As String = String.Empty
        Public Property SelectedContractId As String
            Get
                Return _selectedContractId
            End Get
            Set(value As String)
                If SetProperty(_selectedContractId, value) Then
                    ' Keep _selectedContract in sync so ExecuteStart has TickSize and Name.
                    ' When the user picks from the favourites dropdown the ContractSelectorControl
                    ' sets ContractId → SelectedContractId before SelectedContract can be set,
                    ' so we look up the matching ContractDto from the pre-built _favoriteContracts list.
                    If Not String.IsNullOrWhiteSpace(value) Then
                        Dim match = _favoriteContracts.FirstOrDefault(
                            Function(f) String.Equals(f.ContractId, value, StringComparison.OrdinalIgnoreCase))
                        If match IsNot Nothing Then
                            _selectedContract = match
                        End If
                    End If
                    NotifyPropertyChanged(NameOf(IsFormReady))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        ' ── Risk / quantity ───────────────────────────────────────────────────────
        Private _capitalAtRisk As Decimal = 200D
        Public Property CapitalAtRisk As Decimal
            Get
                Return _capitalAtRisk
            End Get
            Set(value As Decimal)
                SetProperty(_capitalAtRisk, value)
            End Set
        End Property

        Private _quantity As Integer = 1
        Public Property Quantity As Integer
            Get
                Return _quantity
            End Get
            Set(value As Integer)
                If SetProperty(_quantity, value) Then
                    NotifyPropertyChanged(NameOf(IsFormReady))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        ' ── Pre-loaded strategies ─────────────────────────────────────────────────
        Public Property PreloadedStrategies As New ObservableCollection(Of StrategyDefinition)

        Private _selectedPreloaded As StrategyDefinition
        Public Property SelectedPreloaded As StrategyDefinition
            Get
                Return _selectedPreloaded
            End Get
            Set(value As StrategyDefinition)
                If SetProperty(_selectedPreloaded, value) AndAlso value IsNot Nothing Then
                    StrategyText = value.RawDescription
                End If
            End Set
        End Property

        ' ── Strategy text (NL input) ──────────────────────────────────────────────
        Private _strategyText As String = String.Empty
        Public Property StrategyText As String
            Get
                Return _strategyText
            End Get
            Set(value As String)
                SetProperty(_strategyText, value)
            End Set
        End Property

        ' ── Parsed parameters display ─────────────────────────────────────────────
        Private _parsedSummary As String = "— Not yet parsed —"
        Public Property ParsedSummary As String
            Get
                Return _parsedSummary
            End Get
            Set(value As String)
                SetProperty(_parsedSummary, value)
            End Set
        End Property

        Private _hasParsedStrategy As Boolean = False
        Public Property HasParsedStrategy As Boolean
            Get
                Return _hasParsedStrategy
            End Get
            Set(value As Boolean)
                SetProperty(_hasParsedStrategy, value)
            End Set
        End Property

        ' ── Strategy plain-English description ────────────────────────────────────
        ''' <summary>
        ''' Naked-Trader-style prose description of the selected strategy (≤200 words).
        ''' Populated by ApplyEmaRsiCombined() (and future strategy selectors).
        ''' Displayed in the "WHAT THIS STRATEGY DOES" panel below the strategy cards.
        ''' </summary>
        Private _strategyNakedDescription As String = String.Empty
        Public Property StrategyNakedDescription As String
            Get
                Return _strategyNakedDescription
            End Get
            Set(value As String)
                SetProperty(_strategyNakedDescription, value)
            End Set
        End Property

        ''' <summary>True once a strategy card has been selected and the description populated.
        ''' Drives Visibility of the description panel in the XAML.</summary>
        Private _hasStrategyDescription As Boolean = False
        Public Property HasStrategyDescription As Boolean
            Get
                Return _hasStrategyDescription
            End Get
            Set(value As Boolean)
                SetProperty(_hasStrategyDescription, value)
                NotifyPropertyChanged(NameOf(StrategyDescriptionPanelVisible))
            End Set
        End Property

        ''' <summary>Description panel is visible only before monitoring starts.
        ''' Hides when engine is running so the trade table can take its place.</summary>
        Public ReadOnly Property StrategyDescriptionPanelVisible As Boolean
            Get
                Return _hasStrategyDescription AndAlso Not _isRunning
            End Get
        End Property

        ' ── Exit strategy ─────────────────────────────────────────────────────────
        Private _takeProfitPct As Decimal = 4.0D
        ''' <summary>Take-profit as % of entry price. 0 = no TP order. Default 4%.</summary>
        Public Property TakeProfitPct As Decimal
            Get
                Return _takeProfitPct
            End Get
            Set(value As Decimal)
                If SetProperty(_takeProfitPct, Math.Max(0D, value)) Then
                    NotifyPropertyChanged(NameOf(IsFormReady))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        Private _stopLossPct As Decimal = 1.5D
        ''' <summary>Stop-loss as % of entry price. 0 = no SL order. Default 1.5%.</summary>
        Public Property StopLossPct As Decimal
            Get
                Return _stopLossPct
            End Get
            Set(value As Decimal)
                If SetProperty(_stopLossPct, Math.Max(0D, value)) Then
                    NotifyPropertyChanged(NameOf(IsFormReady))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        Private _leverage As Integer = 5
        ''' <summary>Leverage multiplier for eToro orders. Default 5.
        ''' Min trade cash = MinNotionalUsd / leverage.</summary>
        Public Property Leverage As Integer
            Get
                Return _leverage
            End Get
            Set(value As Integer)
                SetProperty(_leverage, Math.Max(1, value))
            End Set
        End Property

        Private _minConfidencePct As Integer = 85
        ''' <summary>
        ''' Minimum signal confidence (0–100) required to fire a trade.
        ''' Passed to StrategyDefinition.MinConfidencePct before the engine starts.
        ''' Default 85 — only fire when the weighted EMA/RSI score is ≥ 85%.
        ''' </summary>
        Public Property MinConfidencePct As Integer
            Get
                Return _minConfidencePct
            End Get
            Set(value As Integer)
                SetProperty(_minConfidencePct, Math.Max(0, Math.Min(100, value)))
            End Set
        End Property

        Private _scaleInAmount As Decimal = 200D
        ''' <summary>Cash amount per scale-in trade. Default $200. Passed to the engine on Start.</summary>
        Public Property ScaleInAmount As Decimal
            Get
                Return _scaleInAmount
            End Get
            Set(value As Decimal)
                SetProperty(_scaleInAmount, Math.Max(1D, value))
            End Set
        End Property

        Private _scaleInLeverage As Integer = 5
        ''' <summary>Leverage multiplier for scale-in trades. Default 5. Passed to the engine on Start.</summary>
        Public Property ScaleInLeverage As Integer
            Get
                Return _scaleInLeverage
            End Get
            Set(value As Integer)
                SetProperty(_scaleInLeverage, Math.Max(1, value))
            End Set
        End Property

        ' ── AI Review ─────────────────────────────────────────────────────────────
        Private _aiReviewText As String = "Click '🤖 Get AI Review' after parsing your strategy."
        Public Property AiReviewText As String
            Get
                Return _aiReviewText
            End Get
            Set(value As String)
                SetProperty(_aiReviewText, value)
            End Set
        End Property

        Private _isReviewing As Boolean = False
        Public Property IsReviewing As Boolean
            Get
                Return _isReviewing
            End Get
            Set(value As Boolean)
                SetProperty(_isReviewing, value)
            End Set
        End Property

        ' ── Running state ─────────────────────────────────────────────────────────
        Private _isRunning As Boolean = False
        Public Property IsRunning As Boolean
            Get
                Return _isRunning
            End Get
            Set(value As Boolean)
                SetProperty(_isRunning, value)
                OnPropertyChanged(NameOf(IsNotRunning))
                OnPropertyChanged(NameOf(StrategyDescriptionPanelVisible))
                OnPropertyChanged(NameOf(LiveConfidenceText))
                OnPropertyChanged(NameOf(LiveConfidenceColor))
                RelayCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        Public ReadOnly Property IsNotRunning As Boolean
            Get
                Return Not _isRunning
            End Get
        End Property

        ' ── Live confidence telemetry ─────────────────────────────────────────────
        ' Updated every 30 seconds by the ConfidenceUpdated engine event.
        ' Displays the dominant EMA/RSI score with directional indicator and colour.
        ' When the ADX gate suppresses the signal, shows amber "⊘ N%" instead of
        ' a green arrow so the user knows the raw score is high but no trade will fire.

        Private _liveConfidencePct As Integer = 0
        Public Property LiveConfidencePct As Integer
            Get
                Return _liveConfidencePct
            End Get
            Set(value As Integer)
                If SetProperty(_liveConfidencePct, value) Then
                    NotifyPropertyChanged(NameOf(LiveConfidenceText))
                    NotifyPropertyChanged(NameOf(LiveConfidenceColor))
                End If
            End Set
        End Property

        Private _liveConfidenceDirection As String = String.Empty
        Public Property LiveConfidenceDirection As String
            Get
                Return _liveConfidenceDirection
            End Get
            Set(value As String)
                SetProperty(_liveConfidenceDirection, value)
            End Set
        End Property

        ''' <summary>True when the last bar check passed the ADX ≥ 25 trend-strength gate.</summary>
        Private _adxGatePassed As Boolean = True
        Public Property AdxGatePassed As Boolean
            Get
                Return _adxGatePassed
            End Get
            Set(value As Boolean)
                If SetProperty(_adxGatePassed, value) Then
                    NotifyPropertyChanged(NameOf(LiveConfidenceText))
                    NotifyPropertyChanged(NameOf(LiveConfidenceColor))
                End If
            End Set
        End Property

        ''' <summary>
        ''' Formatted live score for the UI:
        '''   "↑ 82%"   — ADX gate passed, bullish
        '''   "↓ 31%"   — ADX gate passed, bearish
        '''   "⊘ 100%"  — ADX gate suppressed (ranging market; raw score shown for information)
        '''   "—"       — engine idle or no score yet
        ''' </summary>
        Public ReadOnly Property LiveConfidenceText As String
            Get
                If Not _isRunning OrElse _liveConfidencePct = 0 Then Return "—"
                If Not _adxGatePassed Then Return $"⊘ {_liveConfidencePct}%"
                Dim arrow = If(_liveConfidenceDirection = "UP", "↑", "↓")
                Return $"{arrow} {_liveConfidencePct}%"
            End Get
        End Property

        ''' <summary>
        ''' Colour rules:
        '''   ADX suppressed        → amber  (#FF9500) — signal gated, market ranging
        '''   ADX passed, ≤ 25%     → red    (#E5533A)
        '''   ADX passed, ≥ 85%     → green  (#27AE60)
        '''   ADX passed, otherwise → TextSecondaryColor (#FF8080A0)
        ''' </summary>
        Public ReadOnly Property LiveConfidenceColor As SolidColorBrush
            Get
                If Not _isRunning OrElse _liveConfidencePct = 0 Then
                    Return New SolidColorBrush(Color.FromArgb(&HFF, &H80, &H80, &HA0))
                End If
                If Not _adxGatePassed Then
                    Return New SolidColorBrush(Color.FromRgb(&HFF, &H95, &H00))  ' amber = suppressed
                End If
                If _liveConfidencePct <= 25 Then
                    Return New SolidColorBrush(Color.FromRgb(&HE5, &H53, &H3A))
                End If
                If _liveConfidencePct >= 85 Then
                    Return New SolidColorBrush(Color.FromRgb(&H27, &HAE, &H60))
                End If
                Return New SolidColorBrush(Color.FromArgb(&HFF, &H80, &H80, &HA0))
            End Get
        End Property

        ''' <summary>
        ''' True when account, contract ID, quantity, TP and SL are all set —
        ''' the precondition that must be met before the 5 strategy card buttons enable.
        ''' </summary>
        Public ReadOnly Property IsFormReady As Boolean
            Get
                Return _selectedAccount IsNot Nothing AndAlso
                       Not String.IsNullOrWhiteSpace(_selectedContractId) AndAlso
                       _capitalAtRisk > 0
            End Get
        End Property

        ' ── Active strategy label ──────────────────────────────────────────────────

        Private _activeStrategyText As String = "None selected — click a card above"
        Public Property ActiveStrategyText As String
            Get
                Return _activeStrategyText
            End Get
            Set(value As String)
                SetProperty(_activeStrategyText, value)
            End Set
        End Property

        Private _statusText As String = "● Idle"
        Public Property StatusText As String
            Get
                Return _statusText
            End Get
            Set(value As String)
                SetProperty(_statusText, value)
            End Set
        End Property

        Public Property LogEntries As New ObservableCollection(Of String)

        ' ── Trade performance table ──────────────────────────────────────────────────────
        Public Property TradeRows As New ObservableCollection(Of TradeRowViewModel)

        Private _hasActivePosition As Boolean = False
        Public Property HasActivePosition As Boolean
            Get
                Return _hasActivePosition
            End Get
            Set(value As Boolean)
                SetProperty(_hasActivePosition, value)
            End Set
        End Property

        ''' <summary>True when there are rows to show in the trade history table.</summary>
        Public ReadOnly Property HasTradeRows As Boolean
            Get
                Return TradeRows.Count > 0
            End Get
        End Property

        Private _hasConfidenceResult As Boolean = False
        ''' <summary>True once the confidence check has returned a response. Shows the output TextBox.</summary>
        Public Property HasConfidenceResult As Boolean
            Get
                Return _hasConfidenceResult
            End Get
            Set(value As Boolean)
                SetProperty(_hasConfidenceResult, value)
                NotifyPropertyChanged(NameOf(HasNoResults))
            End Set
        End Property

        ''' <summary>True when neither confidence result nor trades exist — shows placeholder text.</summary>
        Public ReadOnly Property HasNoResults As Boolean
            Get
                Return Not _hasConfidenceResult AndAlso TradeRows.Count = 0
            End Get
        End Property

        ' ── Confidence Check ──────────────────────────────────────────────────────
        Private _confidenceText As String = String.Empty
        Public Property ConfidenceText As String
            Get
                Return _confidenceText
            End Get
            Set(value As String)
                SetProperty(_confidenceText, value)
            End Set
        End Property

        Private _isCheckingConfidence As Boolean = False
        Public Property IsCheckingConfidence As Boolean
            Get
                Return _isCheckingConfidence
            End Get
            Set(value As Boolean)
                SetProperty(_isCheckingConfidence, value)
                RelayCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        ' ── Commands ──────────────────────────────────────────────────────────────
        ' Active strategy button — one-click activate (no parse step required)
        Public ReadOnly Property SelectEmaRsiCombinedCommand As RelayCommand
        Public ReadOnly Property SelectMultiConfluenceEngineCommand As RelayCommand
        Public ReadOnly Property SelectLultDivergenceCommand As RelayCommand

        ' Preserved for future move to Backtest page
        Public ReadOnly Property ParseCommand As RelayCommand
        Public ReadOnly Property GetAiReviewCommand As RelayCommand

        Public ReadOnly Property GetConfidenceCommand As RelayCommand
        Public ReadOnly Property StartCommand As RelayCommand
        Public ReadOnly Property StopCommand As RelayCommand

        ' ── Constructor ───────────────────────────────────────────────────────────

        Public Sub New(accountService As IAccountService,
                       contractClient As ContractClient,
                       parserService As StrategyParserService,
                       reviewService As ClaudeReviewService,
                       engine As StrategyExecutionEngine,
                       tradingOptions As IOptions(Of TradingSettings))
            _accountService = accountService
            _contractClient = contractClient
            _parserService = parserService
            _reviewService = reviewService
            _engine = engine
            _tradingSettings = tradingOptions.Value

            ' Wire engine events
            AddHandler _engine.LogMessage, AddressOf OnEngineLog
            AddHandler _engine.ExecutionStopped, AddressOf OnEngineStopped
            AddHandler _engine.TradeOpened, AddressOf OnTradeOpened
            AddHandler _engine.TradeClosed, AddressOf OnTradeClosed
            AddHandler _engine.BarPriceUpdated, AddressOf OnBarPriceUpdated
            AddHandler _engine.PositionSynced, AddressOf OnPositionSynced
            AddHandler _engine.ConfidenceUpdated, AddressOf OnConfidenceUpdated

            ' Notify HasTradeRows/HasNoResults when the collection changes
            AddHandler TradeRows.CollectionChanged, Sub(s, e)
                                                        NotifyPropertyChanged(NameOf(HasTradeRows))
                                                        NotifyPropertyChanged(NameOf(HasNoResults))
                                                    End Sub

            ' Strategy selection — one-click activate, no parse step required
            SelectEmaRsiCombinedCommand = New RelayCommand(Sub(p) ApplyEmaRsiCombined(),
                                                            Function(p) IsFormReady AndAlso IsNotRunning)
            SelectMultiConfluenceEngineCommand = New RelayCommand(Sub(p) ApplyMultiConfluenceEngine(),
                                                                   Function(p) IsFormReady AndAlso IsNotRunning)
            SelectLultDivergenceCommand = New RelayCommand(Sub(p) ApplyLultDivergence(),
                                                            Function(p) IsFormReady AndAlso IsNotRunning)

            ' Preserved for future move to Backtest page
            ParseCommand = New RelayCommand(AddressOf ExecuteParse)
            GetAiReviewCommand = New RelayCommand(AddressOf ExecuteGetAiReview,
                                                   Function(p) HasParsedStrategy AndAlso Not IsRunning)

            GetConfidenceCommand = New RelayCommand(AddressOf ExecuteGetConfidence,
                                                     Function(p) Not String.IsNullOrWhiteSpace(_selectedContractId) AndAlso Not _isCheckingConfidence)
            StartCommand = New RelayCommand(AddressOf ExecuteStart,
                                                     Function(p) HasParsedStrategy AndAlso Not IsRunning AndAlso SelectedAccount IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(_selectedContractId))
            StopCommand = New RelayCommand(AddressOf ExecuteStop,
                                                     Function(p) IsRunning)

            ' Load pre-loaded strategies
            For Each s In _parserService.PreloadedStrategies
                PreloadedStrategies.Add(s)
            Next

            ' Seed favourite contracts from the shared list (Core\Trading\FavouriteContracts).
            _favoriteContracts = FavouriteContracts.GetDefaults() _
                .Select(Function(f) New ContractDto With {
                    .ContractId  = f.ContractId,
                    .Name        = f.Name,
                    .InstrumentId = f.InstrumentId,
                    .TickSize    = f.TickSize,
                    .TickValue   = f.TickValue
                }).ToList()
            For Each fav In _favoriteContracts
                FilteredContracts.Add(fav)
            Next

            ' Populate AvailableContracts (Contract type) for ContractSelectorControl binding.
            For Each f In FavouriteContracts.GetDefaults()
                AvailableContracts.Add(New Contract With {
                    .Id = f.ContractId,
                    .FriendlyName = f.Name
                })
            Next
        End Sub

        ' ── Data loading ──────────────────────────────────────────────────────────

        Public Async Sub LoadDataAsync()
            Try
                ' Load accounts (use different var name — VB.NET is case-insensitive;
                ' "accounts" would shadow the "Accounts" ObservableCollection property)
                Dim accountList = Await _accountService.GetActiveAccountsAsync()
                Dispatch(Sub()
                             Accounts.Clear()
                             For Each a In accountList
                                 Accounts.Add(a)
                             Next
                             If Accounts.Count > 0 Then
                                 ' Prefer PRAC account, fall back to first
                                 Dim prac = Accounts.FirstOrDefault(
                                     Function(a) a.Name IsNot Nothing AndAlso
                                                 a.Name.StartsWith("PRAC", StringComparison.OrdinalIgnoreCase))
                                 SelectedAccount = If(prac, Accounts(0))
                             End If
                         End Sub)

                ' Contract list is NOT pre-loaded; results arrive via live search as
                ' the user types in the contract ComboBox (SearchContractsAsync).

            Catch ex As Exception
                Dispatch(Sub() StatusText = $"⚠ Load error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' Deep-copy a pre-loaded strategy template (by index in PreloadedStrategies) into
        ''' _currentStrategy, applying the user's current risk/exit settings.
        ''' Called by the 5 strategy card button commands.
        ''' </summary>
        Private Sub ApplyPreloadedByIndex(index As Integer)
            If index < 0 OrElse index >= PreloadedStrategies.Count Then Return

            Dim t = PreloadedStrategies(index)   ' template

            Dim sd As New StrategyDefinition With {
                .Name = t.Name,
                .Indicator = t.Indicator,
                .IndicatorPeriod = t.IndicatorPeriod,
                .IndicatorMultiplier = t.IndicatorMultiplier,
                .SecondaryPeriod = t.SecondaryPeriod,
                .Condition = t.Condition,
                .GoLongWhenBelowBands = t.GoLongWhenBelowBands,
                .GoShortWhenAboveBands = t.GoShortWhenAboveBands,
                .TimeframeMinutes = t.TimeframeMinutes,
                .DurationHours = t.DurationHours,
                .RawDescription = t.RawDescription,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = _quantity,
                .TakeProfitPct = _takeProfitPct,
                .StopLossPct = _stopLossPct,
                .Leverage = _leverage
            }

            _currentStrategy = sd
            HasParsedStrategy = True
            ParsedSummary = sd.Summary
            StrategyText = sd.RawDescription
            ActiveStrategyText = $"✔  {sd.Name}"

            ' Populate the execution log with a strategy briefing.
            ' Log is newest-first (Insert(0,...)), so add lines in reverse display order —
            ' the last LogLine call will appear at the top, the first at the bottom.
            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure your risk settings above, then click  ▶ Start Monitoring.")
            LogLine("")
            LogLine(t.RawDescription)
            LogLine("")
            LogLine($"  {sd.Summary}")
            LogLine($"━━━  {sd.Name}  ━━━")
        End Sub

        ''' <summary>
        ''' One-click activate: builds the EMA/RSI Combined strategy definition directly
        ''' using hardcoded optimal parameters (5-min bars, EMA21/EMA50/RSI14 weighted score).
        ''' Sets HasParsedStrategy = True so Start Monitoring becomes enabled immediately.
        ''' Also auto-fills strategy-recommended TP/SL/Qty defaults and shows a plain-English
        ''' summary panel in Naked Trader prose style.
        ''' </summary>
        Private Sub ApplyEmaRsiCombined()
            Const Description As String =
                "EMA/RSI Combined — 6-signal weighted scoring strategy." & vbLf &
                "Signals: EMA21/EMA50 crossover (25%), price vs EMA21 (20%), price vs EMA50 (15%), " &
                "RSI14 gradient (20%), EMA21 momentum (10%), recent candle pattern (10%)." & vbLf &
                "Timeframe: 5-minute bars. Entry: UP ≥ confidence% → Long, DOWN ≥ confidence% → Short."

            ' ── Apply strategy-recommended defaults ───────────────────────────────
            ' 4% TP / 1.5% SL gives a 2.67:1 risk–reward ratio.
            ' Set via property setters so UI TextBoxes update through normal data binding.
            TakeProfitPct = 4.0D    ' 4% above entry — targets a meaningful intraday momentum move
            StopLossPct = 1.5D     ' 1.5% below entry — tight enough to limit loss on failure
            Leverage = 5           ' 5× leverage matches the scale-in default
            Quantity = 1           ' Kept for reference; by-amount uses CapitalAtRisk

            Dim sd As New StrategyDefinition With {
                .Name = "EMA/RSI Combined",
                .Indicator = Core.Enums.StrategyIndicatorType.EmaRsiCombined,
                .Condition = Core.Enums.StrategyConditionType.EmaRsiWeightedScore,
                .IndicatorPeriod = 50,
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 5,
                .DurationHours = 8,
                .RawDescription = Description,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = _quantity,
                .TakeProfitPct = _takeProfitPct,
                .StopLossPct = _stopLossPct,
                .Leverage = _leverage,
                .ScaleInAmount = _scaleInAmount,
                .ScaleInLeverage = _scaleInLeverage,
                .MinConfidencePct = _minConfidencePct
            }

            _currentStrategy = sd
            HasParsedStrategy = True
            ParsedSummary = "EMA/RSI Combined | 5-min bars | 8-hr session | score ≥ confidence% triggers entry | scale-in at extremes | neutral exits all"
            StrategyText = Description
            ActiveStrategyText = "✔  EMA/RSI Combined  (5-min · 8hrs · EMA21/EMA50/RSI14)"

            ' ── Plain-English description (Naked Trader prose style, ≤200 words) ──
            StrategyNakedDescription =
                "Every 30 seconds, this strategy glances at the latest completed 5-minute bar on " &
                "your chosen contract and runs six quick checks, tallying a bull score from 0 to 100." & vbLf & vbLf &
                "It awards 25 points if the fast moving average (EMA21) sits above the slow one (EMA50) — " &
                "that's your classic uptrend sign. Another 20 points if the closing price is above the fast EMA, " &
                "and 15 more if it's above the slow one too. The RSI14 is also in the mix for up to 20 points — " &
                "a deeply oversold reading (below 30) earns the full amount; an overbought reading (above 70) deducts " &
                "10 points instead, actively pressing the bear side. " &
                "Then 10 points if the fast EMA is rising since the last bar, and a final 10 if at least two of " &
                "the last three candles closed higher." & vbLf & vbLf &
                "When the bull score reaches your confidence threshold (85 by default), a Long fires. When the " &
                "bearish score matches that same threshold — meaning the bull score has dropped to 15 or below at " &
                "the default setting — a Short fires. " &
                "If the score stays deeply extreme (≥ 85 bull or ≤ 25 bear) for 3 consecutive new 5-minute bars, an additional scale-in trade is placed " &
                "(up to 3 scale-ins after the initial, each $200 at 5× leverage). " &
                "The moment the score drifts into the 40–60% neutral band, ALL open positions are closed immediately — " &
                "the trend has lost conviction and the engine gets flat." & vbLf & vbLf &
                "Defaults: 5-min bars · 4% take-profit · 1.5% stop-loss · 2.7:1 R:R · 5× leverage · $200 amount · 85% confidence threshold."
            HasStrategyDescription = True

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account, contract and risk settings above, then click  ▶ Start Monitoring.")
            LogLine("")
            LogLine($"• Defaults: 5-min bars · TP={_takeProfitPct:F1}% · SL={_stopLossPct:F1}% · Amt=${_capitalAtRisk:F0} · Lvg={_leverage}×  · Confidence={_minConfidencePct}%")
            LogLine("• Neutral confidence (40–60%) → ALL positions closed immediately (flatten)")
            LogLine("• Scale-in: 3 more trades at $200/5× after 3 consecutive extreme new bars (cap: 4 total)")
            LogLine("• Entry fires when combined EMA/RSI score ≥ 85% bull (Long) or ≥ 85% bear (Short)")
            LogLine("• 6 weighted signals evaluated on every completed 5-minute bar")
            LogLine("• Duration: 8 hours — covers London open + NY session overlap")
            LogLine("")
            LogLine("━━━  EMA/RSI Combined  ━━━")
        End Sub

        ''' <summary>
        ''' One-click activate: builds the Multi-Confluence Engine strategy definition.
        ''' Uses hardcoded optimal parameters (15-min bars, all 7 conditions required).
        ''' Sets HasParsedStrategy = True so Start Monitoring becomes enabled immediately.
        ''' TP/SL percentage fields are zeroed; the engine uses ATR-derived price levels.
        ''' </summary>
        Private Sub ApplyMultiConfluenceEngine()
            Const Description As String =
                "Multi-Confluence Engine — 7-condition Ichimoku + EMA + MACD + StochRSI + ADX strategy." & vbLf &
                "ALL conditions must align: price above/below Ichimoku Cloud, EMA21, Tenkan/Kijun crossover, " &
                "Lagging Span confirmation, ADX > 25 with DI alignment, MACD histogram direction, StochRSI gate." & vbLf &
                "Timeframe: 15-minute bars. SL = min(1.5×ATR, cloud edge); TP = 2:1 reward-to-risk."

            ' ATR-based dynamic SL/TP — set pct to 0 so the engine uses _mcCloudSlPrice + ATR
            TakeProfitPct = 0D
            StopLossPct = 0D
            Leverage = 5
            Quantity = 1

            Dim sd As New StrategyDefinition With {
                .Name = "Multi-Confluence Engine",
                .Indicator = Core.Enums.StrategyIndicatorType.MultiConfluence,
                .Condition = Core.Enums.StrategyConditionType.MultiConfluence,
                .IndicatorPeriod = 80,
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 15,
                .DurationHours = 8,
                .RawDescription = Description,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = _quantity,
                .TakeProfitPct = 0D,
                .StopLossPct = 0D,
                .Leverage = _leverage,
                .ScaleInAmount = _scaleInAmount,
                .ScaleInLeverage = _scaleInLeverage,
                .MinConfidencePct = _minConfidencePct
            }

            _currentStrategy = sd
            HasParsedStrategy = True
            ParsedSummary = "Multi-Confluence Engine | 15-min bars | 8-hr session | all 7 conditions required | ATR-based dynamic SL/TP"
            StrategyText = Description
            ActiveStrategyText = "✔  Multi-Confluence Engine  (15-min · 8hrs · Ichimoku · EMA21/50 · MACD · StochRSI · ADX)"

            StrategyNakedDescription =
                "Every 30 seconds, this strategy checks the latest completed 15-minute bar on your chosen commodity " &
                "and runs seven independent filters. ALL seven must be green before a trade fires — no exceptions." & vbLf & vbLf &
                "First, the Ichimoku Cloud: price must be completely above both Span A and Span B for a Long, or below both for a Short. " &
                "Being inside the cloud is the 'fog zone' — the engine sits on its hands there. " &
                "The Tenkan-sen (9-period midpoint) must cross above the Kijun-sen (26-period midpoint) for Long. " &
                "The Lagging Span — which plots the current close 26 bars back on the chart — must also confirm the direction." & vbLf & vbLf &
                "Next, trend strength: ADX must be above 25 (the market must be trending, not ranging) and DI+ must exceed DI- for Long. " &
                "Then momentum: the MACD histogram must be positive AND rising — both direction and acceleration required. " &
                "Finally, the Stochastic RSI K line must be below 0.8 for Long (not extremely overbought), above 0.2 for Short." & vbLf & vbLf &
                "When a trade fires: Stop Loss is placed at whichever is closer — 1.5×ATR below entry, or the bottom of the Ichimoku Cloud. " &
                "Take Profit is set at exactly 2× the SL distance (2:1 risk-reward). " &
                "The ATR is fetched from the latest completed bar immediately before the order is placed, so volatility is always current." & vbLf & vbLf &
                "Defaults: 15-min bars · dynamic ATR SL/TP · 8-hr session · 5× leverage."
            HasStrategyDescription = True

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account, contract and risk settings above, then click  ▶ Start Monitoring.")
            LogLine("")
            LogLine($"• Exits: SL = min(1.5×ATR, cloud edge) · TP = 2:1 reward-to-risk (dynamic per-trade ATR)")
            LogLine("• No scale-in: single entry per signal — all 7 conditions must realign for a new entry")
            LogLine("• Entry fires only when ALL 7 conditions align (Ichimoku + EMA21 + Tenkan/Kijun + Chikou + ADX + MACD + StochRSI)")
            LogLine("• 15-minute bars — designed for commodity markets (Gold, Oil)")
            LogLine("")
            LogLine("━━━  Multi-Confluence Engine  ━━━")
        End Sub

        ''' <summary>
        ''' One-click activate: builds the LULT Divergence strategy definition.
        ''' Uses WaveTrend (Market Cipher B) on 5-minute bars with a 6-step gate.
        ''' TP/SL percentage fields are zeroed; the engine derives absolute SL from the
        ''' trigger wave extreme and sets TP at 2R.
        ''' Time filter: 11:00-17:00 UTC (07:00-13:00 EST/EDT - London + NY pre-market).
        ''' Best suited for NQ (Nasdaq 100); select NSDQ100 in the contract picker above.
        ''' </summary>
        Private Sub ApplyLultDivergence()
            Const Description As String =
                "LULT Divergence - 6-step Market Cipher B momentum-price divergence strategy." & vbLf &
                "Steps: Anchor wave (WT1 breaches +-60) -> Trigger wave (shallower - reset if overshoots) -> " &
                "price divergence -> Green/Red Dot (WT1xWT2 cross) -> Engulfing volume candle." & vbLf &
                "Timeframe: 5-minute bars. SL = trigger extreme +/- tick buffer; TP = 2R. " &
                "Time filter: 11:00-17:00 UTC (London + NY pre-market). Optimised for NQ."

            TakeProfitPct = 0D
            StopLossPct = 0D
            Leverage = 5
            Quantity = 1

            Dim sd As New StrategyDefinition With {
                .Name = "LULT Divergence",
                .Indicator = Core.Enums.StrategyIndicatorType.LultDivergence,
                .Condition = Core.Enums.StrategyConditionType.LultDivergence,
                .IndicatorPeriod = 100,
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 5,
                .DurationHours = 8,
                .RawDescription = Description,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = _quantity,
                .TakeProfitPct = 0D,
                .StopLossPct = 0D,
                .Leverage = _leverage,
                .ScaleInAmount = 0D,
                .ScaleInLeverage = 1,
                .MinConfidencePct = _minConfidencePct
            }

            _currentStrategy = sd
            HasParsedStrategy = True
            ParsedSummary = "LULT Divergence | 5-min bars | 8-hr session | 6-step gate | SL = trigger extreme | TP = 2R"
            StrategyText = Description
            ActiveStrategyText = "✔  LULT Divergence  (5-min · 8hrs · WaveTrend · Anchor/Trigger · 2R)"

            StrategyNakedDescription =
                "Every 30 seconds, this strategy looks at the latest completed 5-minute bar and " &
                "runs the WaveTrend oscillator — a faithful simulation of Market Cipher B's blue wave. " &
                "A trade only fires after six conditions pass in strict sequence." & vbLf & vbLf &
                "Step 1–2: The strategy waits for an Anchor wave — the oscillator dives below -60 (oversold) " &
                "for a Long setup, or surges above +60 (overbought) for a Short. Then it waits for a Trigger wave: " &
                "a second oscillator swing in the same direction." & vbLf & vbLf &
                "Step 3: The trigger must be SHALLOWER than the anchor — a higher low for buys, a lower high for sells. " &
                "If the trigger overshoots and goes deeper, the entire setup is scrapped and the engine resets. " &
                "This is the core discipline of the method." & vbLf & vbLf &
                "Step 4: Price divergence must be confirmed — while the oscillator makes a higher low, price must make a LOWER low (Long). " &
                "Momentum is weakening while price is still falling — that is the divergence edge." & vbLf & vbLf &
                "Step 5: A Green Dot (Long) or Red Dot (Short) must appear — the WaveTrend line (WT1) crossing its signal " &
                "line (WT2) at the trigger wave valley or peak. This confirms the momentum reversal is underway." & vbLf & vbLf &
                "Step 6: Finally, a same-direction Engulfing Volume Candle must close — a bullish candle whose body fully " &
                "engulfs the previous bar, with a lower wick no bigger than 40% of the body." & vbLf & vbLf &
                "Entry fires at market on the bar immediately after the engulfing candle. " &
                "Stop loss is placed 2–3 ticks beyond the trigger wave extreme. " &
                "Take profit is set at 2R (twice the SL distance from entry). " &
                "If a historical swing sits between entry and 2R, 50% is closed there and stop moves to breakeven." & vbLf & vbLf &
                "Time filter: 11:00–17:00 UTC only (07:00–13:00 EST/EDT — London open + New York pre-market). " &
                "Signals outside this window are logged but no orders are placed." & vbLf & vbLf &
                "Defaults: 5-min bars · SL at trigger extreme · TP = 2R · 5x leverage · 8-hr session. " &
                "Select NSDQ100 in the contract picker above for best results."
            HasStrategyDescription = True

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account, contract and risk settings above, then click  ▶ Start Monitoring.")
            LogLine("  ⚠  Select NSDQ100 in the contract picker — LULT is optimised for NQ 5-min bars.")
            LogLine("")
            LogLine("• Time filter: 11:00–17:00 UTC (07:00–13:00 EST/EDT) — signals ignored outside window")
            LogLine("• SL = trigger wave extreme ± tick buffer · TP = 2R · Partial TP at nearest swing (50 %)")
            LogLine("• RESET if trigger wave overshoots anchor — engine waits for a clean new setup")
            LogLine("• 6-step gate: Anchor → Trigger (shallower) → Divergence → Dot → Engulfing candle")
            LogLine("")
            LogLine("━━━  LULT Divergence  ━━━")
        End Sub

        ''' <summary>
        ''' Called from the ContractSearchText setter on the UI thread; the actual
        ''' work runs on a ThreadPool thread via Task.Run in the setter.
        ''' Requires at least 2 characters before it issues an API call.
        ''' </summary>
        Private Async Function SearchContractsAsync(searchText As String,
                                                    token As CancellationToken) As Task
            Try
                ' Debounce: wait 300 ms; cancels immediately if the user types again.
                Await Task.Delay(300, token)

                Dim trimmed = If(searchText?.Trim(), String.Empty)
                If trimmed.Length < 2 Then
                    Dispatch(Sub()
                                 FilteredContracts.Clear()
                                 IsContractDropDownOpen = False
                             End Sub)
                    Return
                End If

                Dispatch(Sub() StatusText = $"🔍 Searching '{trimmed}'…")

                Dim resp = Await _contractClient.GetAvailableContractsAsync(trimmed, token)
                If token.IsCancellationRequested Then Return

                Dispatch(Sub()
                             FilteredContracts.Clear()
                             If resp?.Success = True AndAlso
                                resp.Contracts IsNot Nothing AndAlso
                                resp.Contracts.Count > 0 Then
                                 For Each c In resp.Contracts.Take(50)
                                     FilteredContracts.Add(c)
                                 Next
                                 IsContractDropDownOpen = True   ' ← opens the dropdown
                                 StatusText = "● Idle"
                             Else
                                 IsContractDropDownOpen = False
                                 StatusText = $"⚠ No contracts found for '{trimmed}'"
                             End If
                         End Sub)

            Catch ex As OperationCanceledException
                ' Normal: debounce cancelled because the user is still typing.
            Catch ex As Exception
                Dispatch(Sub()
                             IsContractDropDownOpen = False
                             StatusText = $"⚠ Contract search error: {ex.Message}"
                         End Sub)
            End Try
        End Function

        ' ── Commands ──────────────────────────────────────────────────────────────

        Private Sub ExecuteParse(param As Object)
            Dim text = _strategyText?.Trim()
            If String.IsNullOrWhiteSpace(text) Then
                ParsedSummary = "⚠  Please enter a strategy description first."
                Return
            End If

            ' Try pre-loaded match first, then free-text parse
            Dim sd = _parserService.Parse(text)
            If sd Is Nothing Then
                ParsedSummary = "⚠  Could not recognise an indicator in your description. " &
                                "Try mentioning Bollinger Band, RSI, or EMA."
                HasParsedStrategy = False
                Return
            End If

            ' Apply user overrides
            sd.CapitalAtRisk = _capitalAtRisk
            sd.Quantity = _quantity
            sd.TakeProfitPct = _takeProfitPct
            sd.StopLossPct = _stopLossPct
            sd.Leverage = _leverage

            _currentStrategy = sd
            HasParsedStrategy = True
            ParsedSummary = sd.Summary
            LogLine($"Strategy parsed: {sd.Summary}")
        End Sub

        Private Sub ExecuteGetAiReview(param As Object)
            If _currentStrategy Is Nothing Then Return

            IsReviewing = True
            AiReviewText = "🤖 Asking Claude for suggestions..."

            Task.Run(Async Function()
                         Try
                             Dim result = Await _reviewService.ReviewStrategyAsync(_currentStrategy)
                             Dispatch(Sub() AiReviewText = result)
                         Catch ex As Exception
                             Dispatch(Sub() AiReviewText = $"⚠  Review failed: {ex.Message}")
                         Finally
                             Dispatch(Sub() IsReviewing = False)
                         End Try
                     End Function)
        End Sub

        Private Sub ExecuteGetConfidence(param As Object)
            If String.IsNullOrWhiteSpace(_selectedContractId) Then Return

            IsCheckingConfidence = True
            ConfidenceText = "🤖 Asking Claude for market context..."

            Dim contractId = _selectedContractId.Trim()

            Task.Run(Async Function()
                         Try
                             Dim result = Await _reviewService.ConfidenceCheckAsync(contractId)
                             Dispatch(Sub()
                                          ConfidenceText = result
                                          HasConfidenceResult = True
                                      End Sub)
                         Catch ex As Exception
                             Dispatch(Sub() ConfidenceText = $"⚠  Confidence check failed: {ex.Message}")
                         Finally
                             Dispatch(Sub() IsCheckingConfidence = False)
                         End Try
                     End Function)
        End Sub

        Private Sub ExecuteStart(param As Object)
            If _currentStrategy Is Nothing OrElse SelectedAccount Is Nothing OrElse String.IsNullOrWhiteSpace(_selectedContractId) Then Return

            ' Finalise strategy with selected account, contract, exit params, and confidence threshold
            _currentStrategy.AccountId = SelectedAccount.Id
            _currentStrategy.ContractId = _selectedContractId.Trim()
            _currentStrategy.CapitalAtRisk = _capitalAtRisk
            _currentStrategy.Quantity = _quantity
            _currentStrategy.TakeProfitPct = _takeProfitPct
            _currentStrategy.StopLossPct = _stopLossPct
            _currentStrategy.Leverage = _leverage
            _currentStrategy.MinConfidencePct = _minConfidencePct
            _currentStrategy.ScaleInAmount = _scaleInAmount
            _currentStrategy.ScaleInLeverage = _scaleInLeverage

            LogEntries.Clear()
            Dispatch(Sub()
                         TradeRows.Clear()
                         HasActivePosition = False
                         HasConfidenceResult = False
                         ConfidenceText = String.Empty
                     End Sub)
            IsRunning = True

            ' Build a descriptive status: "● Running — MESH26 — Micro S&P 500 | EMA Smush Zone"
            Dim shortId = _currentStrategy.ContractId.
                              Replace("CON.F.US.", String.Empty).
                              Replace(".", String.Empty)
            Dim rawName = If(_selectedContract IsNot Nothing AndAlso
                             Not String.IsNullOrWhiteSpace(_selectedContract.Name),
                             _selectedContract.Name, shortId)
            ' Favourites have names like "MESH26 — Micro S&P 500"; strip the "MESH26 — " prefix
            Dim sepIdx = rawName.IndexOf("—")
            Dim contractLabel = If(sepIdx > 0, rawName.Substring(sepIdx + 2).Trim(), rawName)
            StatusText = $"● Running — {shortId} — {contractLabel} | {_currentStrategy.Name}"

            _engine.Start(_currentStrategy)
        End Sub

        Private Sub ExecuteStop(param As Object)
            _engine.Stop()
        End Sub

        ' ── Engine event handlers ─────────────────────────────────────────────────

        Private Sub OnEngineLog(sender As Object, message As String)
            Dispatch(Sub() LogLine(message))
        End Sub

        Private Sub OnEngineStopped(sender As Object, reason As String)
            Dispatch(Sub()
                         IsRunning = False
                         StatusText = "● Idle"
                         LiveConfidencePct = 0
                         LiveConfidenceDirection = String.Empty
                         LogLine($"Engine stopped: {reason}")
                     End Sub)
        End Sub

        Private Sub OnTradeOpened(sender As Object, e As Core.Events.TradeOpenedEventArgs)
            Dispatch(Sub()
                         Dim row As New TradeRowViewModel(e.Side, e.ContractId, e.ConfidencePct,
                                                          e.EntryTime, e.ExternalOrderId,
                                                          e.EtoroPositionId, e.OpenedAtUtc,
                                                          e.Amount, e.Leverage, e.EntryPrice)
                         TradeRows.Insert(0, row)
                         HasActivePosition = True
                         NotifyPropertyChanged(NameOf(HasTradeRows))
                     End Sub)
        End Sub

        Private Sub OnTradeClosed(sender As Object, e As Core.Events.TradeClosedEventArgs)
            Dispatch(Sub()
                         ' Close the first in-progress row.  The engine fires this event once per
                         ' tracked open trade (initial + each scale-in), so sequential calls here
                         ' will walk through and reconcile every stale "In Progress" UI row.
                         Dim openRow = TradeRows.FirstOrDefault(Function(r) r.IsInProgress)
                         openRow?.Close(e.ExitReason, e.PnL)
                         HasActivePosition = TradeRows.Any(Function(r) r.IsInProgress)
                     End Sub)
        End Sub

        Private Sub OnBarPriceUpdated(sender As Object, currentPrice As Decimal)
            Dispatch(Sub()
                         For Each row In TradeRows
                             If row.IsInProgress Then row.UpdatePnl(currentPrice)
                         Next
                     End Sub)
        End Sub

        Private Sub OnPositionSynced(sender As Object, e As Core.Events.PositionSyncedEventArgs)
            Dispatch(Sub()
                         Dim openRow = TradeRows.FirstOrDefault(Function(r) r.IsInProgress)
                         openRow?.ApplyApiSnapshot(e.PositionId, e.UnrealizedPnlUsd, e.OpenedAtUtc)
                     End Sub)
        End Sub

        Private Sub OnConfidenceUpdated(sender As Object, e As Core.Events.ConfidenceUpdatedEventArgs)
            Dispatch(Sub()
                         Dim dominant = If(e.UpPct >= e.DownPct, "UP", "DOWN")
                         Dim dominantPct = If(e.UpPct >= e.DownPct, e.UpPct, e.DownPct)
                         LiveConfidenceDirection = dominant
                         LiveConfidencePct = dominantPct
                         AdxGatePassed = e.AdxGatePassed
                     End Sub)
        End Sub

        Private Sub LogLine(message As String)
            LogEntries.Insert(0, message)
            ' Keep the log bounded to avoid memory growth — trim oldest entries (now at the bottom)
            Do While LogEntries.Count > 500
                LogEntries.RemoveAt(LogEntries.Count - 1)
            Loop
        End Sub

        ' ── Helpers ───────────────────────────────────────────────────────────────

        Private Sub Dispatch(action As Action)
            If Application.Current?.Dispatcher IsNot Nothing Then
                Application.Current.Dispatcher.Invoke(action)
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                RemoveHandler _engine.LogMessage, AddressOf OnEngineLog
                RemoveHandler _engine.ExecutionStopped, AddressOf OnEngineStopped
                RemoveHandler _engine.TradeOpened, AddressOf OnTradeOpened
                RemoveHandler _engine.TradeClosed, AddressOf OnTradeClosed
                RemoveHandler _engine.BarPriceUpdated, AddressOf OnBarPriceUpdated
                RemoveHandler _engine.PositionSynced, AddressOf OnPositionSynced
                RemoveHandler _engine.ConfidenceUpdated, AddressOf OnConfidenceUpdated
                _searchCts?.Cancel()
                _searchCts?.Dispose()
                _engine.Dispose()
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
