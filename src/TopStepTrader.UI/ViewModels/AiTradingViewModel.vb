Imports System.Collections.ObjectModel
Imports System.Threading
Imports System.Windows
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
        Private _capitalAtRisk As Decimal = 500D
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
        Private _takeProfitTicks As Integer = 40
        Public Property TakeProfitTicks As Integer
            Get
                Return _takeProfitTicks
            End Get
            Set(value As Integer)
                If SetProperty(_takeProfitTicks, value) Then
                    NotifyPropertyChanged(NameOf(IsFormReady))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        Private _stopLossTicks As Integer = 20
        Public Property StopLossTicks As Integer
            Get
                Return _stopLossTicks
            End Get
            Set(value As Integer)
                If SetProperty(_stopLossTicks, value) Then
                    NotifyPropertyChanged(NameOf(IsFormReady))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        Private _minConfidencePct As Integer = 75
        ''' <summary>
        ''' Minimum signal confidence (0–100) required to fire a trade.
        ''' Passed to StrategyDefinition.MinConfidencePct before the engine starts.
        ''' Default 75 — only fire when the weighted EMA/RSI score is ≥ 75%.
        ''' </summary>
        Public Property MinConfidencePct As Integer
            Get
                Return _minConfidencePct
            End Get
            Set(value As Integer)
                SetProperty(_minConfidencePct, Math.Max(0, Math.Min(100, value)))
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
                RelayCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        Public ReadOnly Property IsNotRunning As Boolean
            Get
                Return Not _isRunning
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
                       _quantity > 0 AndAlso
                       _takeProfitTicks > 0 AndAlso
                       _stopLossTicks > 0
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

            ' Notify HasTradeRows/HasNoResults when the collection changes
            AddHandler TradeRows.CollectionChanged, Sub(s, e)
                                                        NotifyPropertyChanged(NameOf(HasTradeRows))
                                                        NotifyPropertyChanged(NameOf(HasNoResults))
                                                    End Sub

            ' Strategy selection — one-click activate, no parse step required
            SelectEmaRsiCombinedCommand = New RelayCommand(Sub(p) ApplyEmaRsiCombined(),
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
                .TakeProfitTicks = _takeProfitTicks,
                .StopLossTicks = _stopLossTicks
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
            ' These are optimal settings for EMA/RSI Combined on micro-futures at 5-min bars.
            ' Set via property setters so UI TextBoxes update through normal data binding.
            TakeProfitTicks = 40    ' ~2× average 5-min ATR on micro-futures; achievable without over-reaching
            StopLossTicks = 20    ' 1:2 risk/reward ratio; protects capital without premature exit
            Quantity = 1     ' Conservative default; user can increase after reviewing results

            Dim sd As New StrategyDefinition With {
                .Name = "EMA/RSI Combined",
                .Indicator = Core.Enums.StrategyIndicatorType.EmaRsiCombined,
                .Condition = Core.Enums.StrategyConditionType.EmaRsiWeightedScore,
                .IndicatorPeriod = 50,    ' larger EMA period — drives min-bar guard in engine
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 5,     ' optimal for micro-futures intraday
                .DurationHours = 8,     ' covers London+NY sessions
                .RawDescription = Description,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = _quantity,      ' now = 1 (recommended)
                .TakeProfitTicks = _takeProfitTicks,  ' now = 40 (recommended)
                .StopLossTicks = _stopLossTicks     ' now = 20 (recommended)
            }

            _currentStrategy = sd
            HasParsedStrategy = True
            ParsedSummary = "EMA/RSI Combined | 5-min bars | 8-hr session | score ≥ confidence% triggers entry"
            StrategyText = Description
            ActiveStrategyText = "✔  EMA/RSI Combined  (5-min · 8hrs · EMA21/EMA50/RSI14)"

            ' ── Plain-English description (Naked Trader prose style, ≤200 words) ──
            StrategyNakedDescription =
                "Every 30 seconds, this strategy glances at the latest completed 5-minute bar on " &
                "your chosen contract and runs six quick checks, tallying a bull score from 0 to 100." & vbLf & vbLf &
                "It awards 25 points if the fast moving average (EMA21) sits above the slow one (EMA50) — " &
                "that's your classic uptrend sign. Another 20 points if the closing price is above the fast EMA, " &
                "and 15 more if it's above the slow one too. The RSI14 is also in the mix for up to 20 points — " &
                "a deeply oversold reading (below 30) earns the full amount; an overbought one (above 70) earns nothing. " &
                "Then 10 points if the fast EMA is rising since the last bar, and a final 10 if at least two of " &
                "the last three candles closed higher." & vbLf & vbLf &
                "When the total bull score hits your confidence threshold — 75 by default — a market Long order " &
                "fires straight away, bracketed by your take-profit above and stop-loss below. If the bear score " &
                "reaches the threshold first (bull score below 25), a Short goes in instead." & vbLf & vbLf &
                "Recommended defaults: 5-minute bars · 40-tick take-profit · 20-tick stop-loss · 1 contract. " &
                "The engine runs for 8 hours, covering both London open and New York session overlap."
            HasStrategyDescription = True

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account, contract and risk settings above, then click  ▶ Start Monitoring.")
            LogLine("")
            LogLine($"• Recommended: 5-min bars · TP={_takeProfitTicks}t · SL={_stopLossTicks}t · Qty={_quantity}")
            LogLine("• Entry fires when combined EMA/RSI score ≥ confidence% bull (Long) or ≥ confidence% bear (Short)")
            LogLine("• 6 weighted signals evaluated on every completed 5-minute bar")
            LogLine("• Duration: 8 hours — covers London open + NY session overlap")
            LogLine("")
            LogLine("━━━  EMA/RSI Combined  ━━━")
        End Sub

        ''' <summary>
        ''' Live contract search with 300 ms debounce.
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
            sd.TakeProfitTicks = _takeProfitTicks
            sd.StopLossTicks = _stopLossTicks

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
            _currentStrategy.TakeProfitTicks = _takeProfitTicks
            _currentStrategy.StopLossTicks = _stopLossTicks
            _currentStrategy.TickSize = If(_selectedContract IsNot Nothing AndAlso _selectedContract.TickSize > 0, _selectedContract.TickSize, 1D)
            _currentStrategy.MinConfidencePct = _minConfidencePct

            ' Resolve TickValue from FavouriteContracts for P&L calculation in performance panel
            Dim favMatch = Core.Trading.FavouriteContracts.GetDefaults().FirstOrDefault(
                Function(f) _selectedContractId.IndexOf(f.ContractId.Substring(0, Math.Min(10, f.ContractId.Length)),
                                                         StringComparison.OrdinalIgnoreCase) >= 0)
            _currentStrategy.TickValue = If(favMatch IsNot Nothing AndAlso favMatch.TickValue > 0, favMatch.TickValue, 1D)

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
                         LogLine($"Engine stopped: {reason}")
                     End Sub)
        End Sub

        Private Sub OnTradeOpened(sender As Object, e As Core.Events.TradeOpenedEventArgs)
            Dispatch(Sub()
                         Dim row As New TradeRowViewModel(e.Side, e.ContractId, e.ConfidencePct,
                                                          e.EntryTime, e.ExternalOrderId)
                         TradeRows.Insert(0, row)
                         HasActivePosition = True
                         NotifyPropertyChanged(NameOf(HasTradeRows))
                     End Sub)
        End Sub

        Private Sub OnTradeClosed(sender As Object, e As Core.Events.TradeClosedEventArgs)
            Dispatch(Sub()
                         ' Update the most-recent (top) row with the result
                         If TradeRows.Count > 0 Then
                             TradeRows(0).Close(e.ExitReason, e.PnL)
                         End If
                         HasActivePosition = False
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
                _searchCts?.Cancel()
                _searchCts?.Dispose()
                _engine.Dispose()
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
