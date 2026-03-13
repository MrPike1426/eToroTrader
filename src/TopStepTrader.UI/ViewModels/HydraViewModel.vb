Imports System.Collections.ObjectModel
Imports System.Windows
Imports Microsoft.Extensions.DependencyInjection
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Services.Trading
Imports TopStepTrader.UI.ViewModels.Base

Namespace TopStepTrader.UI.ViewModels

    ''' <summary>
    ''' ViewModel for the Hydra multi-asset monitoring view.
    ''' Runs 5 independent EMA/RSI Combined sessions (one per asset) concurrently and
    ''' surfaces per-asset confidence snapshots in real time.  No session expiry — monitors 24/7.
    ''' Each engine runs in its own DI scope so BarIngestionService and IOrderService are
    ''' fully isolated between assets.
    ''' </summary>
    Public Class HydraViewModel
        Inherits ViewModelBase
        Implements IDisposable

        ' ── Asset roster (fixed 5 instruments) ───────────────────────────────────
        Private Shared ReadOnly Symbols As String() = {"OIL", "GOLD", "NSDQ100", "SPX500", "UK100"}
        Private Shared ReadOnly Icons As String() = {"🛢️", "🥇", "📈", "🏦", "🇬🇧"}

        ' ── Dependencies ──────────────────────────────────────────────────────────
        Private ReadOnly _scopeFactory As IServiceScopeFactory
        Private ReadOnly _accountService As IAccountService

        ' ── Per-asset scope + engine ──────────────────────────────────────────────
        Private ReadOnly _assetScopes(4) As IServiceScope
        Private ReadOnly _engines(4) As StrategyExecutionEngine

        ' ── Internal state ────────────────────────────────────────────────────────
        Private _currentStrategy As StrategyDefinition
        Private _disposed As Boolean = False

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
                RelayCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        ''' <summary>
        ''' Pre-select the account already chosen on the Dashboard.
        ''' Mirrors AiTradingViewModel.SyncDashboardAccount.
        ''' </summary>
        Public Sub SyncDashboardAccount(account As Account)
            If account Is Nothing Then Return
            If Not Accounts.Any(Function(a) a.Id = account.Id) Then
                Accounts.Insert(0, account)
            End If
            Dim match = Accounts.FirstOrDefault(Function(a) a.Id = account.Id)
            SelectedAccount = If(match IsNot Nothing, match, account)
        End Sub

        ' ── Asset VMs ─────────────────────────────────────────────────────────────
        Public Property Assets As New ObservableCollection(Of HydraAssetViewModel)

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

        Private _leverage As Integer = 5
        Public Property Leverage As Integer
            Get
                Return _leverage
            End Get
            Set(value As Integer)
                SetProperty(_leverage, Math.Max(1, value))
            End Set
        End Property

        Private _initialTpAmount As Decimal = 20D
        ''' <summary>Initial take-profit in dollars. Turtle bracket first TP level. Default $20.</summary>
        Public Property InitialTpAmount As Decimal
            Get
                Return _initialTpAmount
            End Get
            Set(value As Decimal)
                SetProperty(_initialTpAmount, Math.Max(0D, value))
            End Set
        End Property

        Private _initialSlAmount As Decimal = 10D
        ''' <summary>Initial stop-loss in dollars. Turtle bracket first SL level. Default $10.</summary>
        Public Property InitialSlAmount As Decimal
            Get
                Return _initialSlAmount
            End Get
            Set(value As Decimal)
                SetProperty(_initialSlAmount, Math.Max(0D, value))
            End Set
        End Property

        Private _minConfidencePct As Integer = 85
        Public Property MinConfidencePct As Integer
            Get
                Return _minConfidencePct
            End Get
            Set(value As Integer)
                SetProperty(_minConfidencePct, Math.Max(0, Math.Min(100, value)))
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
                NotifyPropertyChanged(NameOf(LastUpdatedDisplay))
                RelayCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        Public ReadOnly Property IsNotRunning As Boolean
            Get
                Return Not _isRunning
            End Get
        End Property

        ' ── Strategy selection ────────────────────────────────────────────────────
        Private _hasParsedStrategy As Boolean = False
        Public Property HasParsedStrategy As Boolean
            Get
                Return _hasParsedStrategy
            End Get
            Set(value As Boolean)
                SetProperty(_hasParsedStrategy, value)
                RelayCommand.RaiseCanExecuteChanged()
            End Set
        End Property

        Private _activeStrategyText As String = "None selected — click a card above"
        Public Property ActiveStrategyText As String
            Get
                Return _activeStrategyText
            End Get
            Set(value As String)
                SetProperty(_activeStrategyText, value)
            End Set
        End Property

        ' ── Active strategy kind (drives DataGrid column switching in the view) ──
        Private _activeStrategyKind As String = "None"
        Public Property ActiveStrategyKind As String
            Get
                Return _activeStrategyKind
            End Get
            Set(value As String)
                If SetProperty(_activeStrategyKind, value) Then
                    NotifyPropertyChanged(NameOf(IsEmaRsiActive))
                    NotifyPropertyChanged(NameOf(IsMultiConfluenceActive))
                    NotifyPropertyChanged(NameOf(IsOtherStrategyActive))
                End If
            End Set
        End Property

        Public ReadOnly Property IsEmaRsiActive As Boolean
            Get
                Return _activeStrategyKind = "EmaRsi"
            End Get
        End Property

        Public ReadOnly Property IsMultiConfluenceActive As Boolean
            Get
                Return _activeStrategyKind = "MultiConfluence"
            End Get
        End Property

        Public ReadOnly Property IsOtherStrategyActive As Boolean
            Get
                Return _activeStrategyKind = "Other"
            End Get
        End Property

        Public ReadOnly Property IsFormReady As Boolean
            Get
                Return _selectedAccount IsNot Nothing
            End Get
        End Property

        ' ── Status / Log ──────────────────────────────────────────────────────────
        Private _statusText As String = "● Select a strategy"
        Public Property StatusText As String
            Get
                Return _statusText
            End Get
            Set(value As String)
                SetProperty(_statusText, value)
            End Set
        End Property

        Private _lastUpdatedAt As String = String.Empty
        Public Property LastUpdatedAt As String
            Get
                Return _lastUpdatedAt
            End Get
            Set(value As String)
                If SetProperty(_lastUpdatedAt, value) Then
                    NotifyPropertyChanged(NameOf(LastUpdatedDisplay))
                End If
            End Set
        End Property

        ''' <summary>
        ''' Shows "  Last Updated: HH:mm:ss" next to the running status; empty when not running.
        ''' </summary>
        Public ReadOnly Property LastUpdatedDisplay As String
            Get
                If _isRunning AndAlso Not String.IsNullOrEmpty(_lastUpdatedAt) Then
                    Return $"   Last Updated: {_lastUpdatedAt}"
                End If
                Return String.Empty
            End Get
        End Property

        Public Property LogEntries As New ObservableCollection(Of String)

        ' ── Commands ──────────────────────────────────────────────────────────────
        Public ReadOnly Property SelectEmaRsiCombinedCommand As RelayCommand
        Public ReadOnly Property SelectMultiConfluenceEngineCommand As RelayCommand
        Public ReadOnly Property SelectLultDivergenceCommand As RelayCommand
        Public ReadOnly Property SelectBbSqueezeScalperCommand As RelayCommand
        Public ReadOnly Property StartCommand As RelayCommand
        Public ReadOnly Property StopCommand As RelayCommand

        ' ── Constructor ───────────────────────────────────────────────────────────

        Public Sub New(scopeFactory As IServiceScopeFactory,
                       accountService As IAccountService)
            _scopeFactory = scopeFactory
            _accountService = accountService

            ' Build 5 per-asset ViewModels
            For i = 0 To 4
                Assets.Add(New HydraAssetViewModel(Symbols(i), Icons(i), Symbols(i)))
            Next

            ' Create 5 independent DI scopes so each engine gets its own
            ' BarIngestionService and IOrderService (both Scoped).
            For i = 0 To 4
                _assetScopes(i) = _scopeFactory.CreateScope()
                _engines(i) = _assetScopes(i).ServiceProvider _
                                  .GetRequiredService(Of StrategyExecutionEngine)()
                ' Capture loop variable for the lambda — VB.NET closes over the variable
                ' itself, not the value, so an explicit capture is required.
                Dim capturedVm = Assets(i)
                _engines(i).IsOrderingAllowed = Function() capturedVm.IsMarketOpen
                WireEngineEvents(_engines(i), Assets(i))
            Next

            SelectEmaRsiCombinedCommand = New RelayCommand(
                Sub(p) ApplyEmaRsiCombined(),
                Function(p) IsFormReady AndAlso IsNotRunning)

            SelectMultiConfluenceEngineCommand = New RelayCommand(
                Sub(p) ApplyMultiConfluenceEngine(),
                Function(p) IsFormReady AndAlso IsNotRunning)

            SelectLultDivergenceCommand = New RelayCommand(
                Sub(p) ApplyLultDivergence(),
                Function(p) IsFormReady AndAlso IsNotRunning)

            SelectBbSqueezeScalperCommand = New RelayCommand(
                Sub(p) ApplyBbSqueezeScalper(),
                Function(p) IsFormReady AndAlso IsNotRunning)

            StartCommand = New RelayCommand(
                AddressOf ExecuteStart,
                Function(p) HasParsedStrategy AndAlso IsNotRunning AndAlso SelectedAccount IsNot Nothing)

            StopCommand = New RelayCommand(
                AddressOf ExecuteStop,
                Function(p) IsRunning)
        End Sub

        ' ── Engine event wiring ───────────────────────────────────────────────────

        Private Sub WireEngineEvents(engine As StrategyExecutionEngine,
                                     assetVm As HydraAssetViewModel)
            AddHandler engine.ConfidenceUpdated,
                Sub(s As Object, e As ConfidenceUpdatedEventArgs)
                    Dispatch(Sub()
                                 assetVm.ApplyConfidence(e)
                                 LastUpdatedAt = DateTime.Now.ToString("HH:mm:ss")
                             End Sub)
                End Sub

            AddHandler engine.LogMessage,
                Sub(s As Object, msg As String)
                    Dispatch(Sub() LogLine($"[{assetVm.Symbol}] {msg}"))
                End Sub

            AddHandler engine.ExecutionStopped,
                Sub(s As Object, reason As String)
                    Dispatch(Sub() LogLine($"[{assetVm.Symbol}] ■ Stopped: {reason}"))
                End Sub

            AddHandler engine.TradeOpened,
                Sub(s As Object, e As TradeOpenedEventArgs)
                    Dispatch(Sub()
                                 assetVm.OpenTrade(e.Side, e.EntryPrice, e.Amount, e.Leverage)
                                 LogLine($"[{assetVm.Symbol}] 🟢 Trade opened — {e.Side} @ {e.EntryPrice:F4} | ${e.Amount:F0} × {e.Leverage}x")
                             End Sub)
                End Sub

            AddHandler engine.TradeClosed,
                Sub(s As Object, e As TradeClosedEventArgs)
                    Dispatch(Sub()
                                 assetVm.CloseTrade()
                                 LogLine($"[{assetVm.Symbol}] 🔴 Trade closed — {e.ExitReason} | P&L={If(e.PnL >= 0D, "+", "")}${e.PnL:F2}")
                             End Sub)
                End Sub

            AddHandler engine.PositionSynced,
                Sub(s As Object, e As PositionSyncedEventArgs)
                    Dispatch(Sub() assetVm.UpdateTradePnl(e.UnrealizedPnlUsd))
                End Sub

            AddHandler engine.TurtleBracketChanged,
                Sub(s As Object, e As TurtleBracketChangedEventArgs)
                    Dispatch(Sub() assetVm.ApplyTurtleBracket(e.BracketNumber, e.SlPrice, e.TpPrice, e.IsAdvance))
                End Sub
        End Sub

        ' ── Data loading ──────────────────────────────────────────────────────────

        Public Async Sub LoadDataAsync()
            Try
                Dim accountList = Await _accountService.GetActiveAccountsAsync()
                Dispatch(Sub()
                             Accounts.Clear()
                             For Each a In accountList
                                 Accounts.Add(a)
                             Next
                             If Accounts.Count > 0 Then
                                 Dim prac = Accounts.FirstOrDefault(
                                     Function(a) a.Name IsNot Nothing AndAlso
                                                 a.Name.StartsWith("PRAC", StringComparison.OrdinalIgnoreCase))
                                 SelectedAccount = If(prac, Accounts(0))
                             End If
                         End Sub)
                Await CheckExistingPositionsAsync()
            Catch ex As Exception
                Dispatch(Sub() StatusText = $"⚠ Load error: {ex.Message}")
            End Try
        End Sub

        ''' <summary>
        ''' On view load, queries the eToro portfolio API for each of the 5 monitored assets
        ''' and pre-populates any asset tile that already has an open position.
        ''' This surfaces trades that were placed outside the engine (manual trades or a
        ''' previous session) so the UI is never left showing "No position" incorrectly.
        ''' </summary>
        Private Async Function CheckExistingPositionsAsync() As Task
            Dim accountId As Long = If(SelectedAccount IsNot Nothing, SelectedAccount.Id, 0L)
            For i = 0 To 4
                Try
                    Dim orderService = _assetScopes(i).ServiceProvider.GetRequiredService(Of IOrderService)()
                    Dim snapshot = Await orderService.GetLivePositionSnapshotAsync(accountId, Assets(i).ContractId)
                    If snapshot IsNot Nothing Then
                        Dim side = If(snapshot.IsBuy, OrderSide.Buy, OrderSide.Sell)
                        Dim capturedVm = Assets(i)
                        Dim capturedSnap = snapshot
                        Dispatch(Sub()
                                     capturedVm.OpenTrade(side, capturedSnap.OpenRate, capturedSnap.Amount, capturedSnap.Leverage)
                                     If capturedSnap.UnrealizedPnlUsd <> 0D Then
                                         capturedVm.UpdateTradePnl(capturedSnap.UnrealizedPnlUsd)
                                     End If
                                 End Sub)
                    End If
                Catch ex As Exception
                    ' Non-fatal — a single asset failing should not block the others
                End Try
            Next
        End Function

        ' ── Strategy activation ───────────────────────────────────────────────────

        ''' <summary>
        ''' Activates the EMA/RSI Combined strategy for all 5 assets.
        ''' DurationHours = 8 760 (one calendar year) so sessions never auto-expire
        ''' — satisfying the "runs 24/7" requirement.
        ''' </summary>
        Private Sub ApplyEmaRsiCombined()
            InitialTpAmount = 20D
            InitialSlAmount = 10D
            Leverage = 5

            _currentStrategy = New StrategyDefinition With {
                .Name = "EMA/RSI Combined",
                .Indicator = StrategyIndicatorType.EmaRsiCombined,
                .Condition = StrategyConditionType.EmaRsiWeightedScore,
                .IndicatorPeriod = 50,
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 5,
                .DurationHours = 8760,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = 1,
                .InitialTpAmount = _initialTpAmount,
                .InitialSlAmount = _initialSlAmount,
                .Leverage = _leverage,
                .ScaleInAmount = 200D,
                .ScaleInLeverage = 5,
                .MinConfidencePct = _minConfidencePct
            }

            HasParsedStrategy = True
            ActiveStrategyText = "✔  EMA/RSI Combined  (5-min · 24/7 · EMA21/EMA50/RSI14)"
            ActiveStrategyKind = "EmaRsi"

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account + risk settings above, then click  ▶ Start Monitoring.")
            LogLine($"• 5-min bars · TP=${_initialTpAmount:F0} · SL=${_initialSlAmount:F0} · Amt=${_capitalAtRisk:F0} · Lvg={_leverage}× · Confidence={_minConfidencePct}%")
            LogLine("• 5 independent sessions — OIL · GOLD · NSDQ100 · SPX500 · UK100")
            LogLine("━━━  EMA/RSI Combined — Hydra 5-Asset Monitor  ━━━")
        End Sub

        ''' <summary>
        ''' Activates the Multi-Confluence Engine strategy for all 5 assets.
        ''' Uses Turtle bracket (InitialTpAmount = $20, InitialSlAmount = $10) as the
        ''' initial bracket; bracket advances on each TP hit using 0.5×N ATR steps.
        ''' DurationHours = 8 760 so sessions never auto-expire.
        ''' </summary>
        Private Sub ApplyMultiConfluenceEngine()
            InitialTpAmount = 20D
            InitialSlAmount = 10D
            Leverage = 5

            _currentStrategy = New StrategyDefinition With {
                .Name = "Multi-Confluence Engine",
                .Indicator = StrategyIndicatorType.MultiConfluence,
                .Condition = StrategyConditionType.MultiConfluence,
                .IndicatorPeriod = 80,
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 15,
                .DurationHours = 8760,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = 1,
                .InitialTpAmount = _initialTpAmount,
                .InitialSlAmount = _initialSlAmount,
                .Leverage = _leverage,
                .ScaleInAmount = 200D,
                .ScaleInLeverage = 5,
                .MinConfidencePct = _minConfidencePct
            }

            HasParsedStrategy = True
            ActiveStrategyText = "✔  Multi-Confluence Engine  (15-min · 24/7 · Ichimoku · EMA21/50 · MACD · StochRSI · ADX)"
            ActiveStrategyKind = "MultiConfluence"

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account + risk settings above, then click  ▶ Start Monitoring.")
            LogLine("\u2022 SL = min(1.5×ATR, cloud edge) · TP = 2:1 R:R (dynamic per-trade ATR)")
            LogLine("• Entry fires only when ALL 7 conditions align (Ichimoku + EMA21 + Tenkan/Kijun + Chikou + ADX + MACD + StochRSI)")
            LogLine("• 5 independent sessions — OIL · GOLD · NSDQ100 · SPX500 · UK100")
            LogLine("━━━  Multi-Confluence Engine — Hydra 5-Asset Monitor  ━━━")
        End Sub

        ''' <summary>
        ''' Activates the LULT Divergence strategy for all 5 assets.
        ''' Uses WaveTrend (Market Cipher B) Anchor/Trigger divergence on 5-minute bars.
        ''' Uses Turtle bracket (InitialTpAmount = $20, InitialSlAmount = $10) with
        ''' LULT-specific ATR-derived SL/TP anchored to WaveTrend divergence levels.
        ''' Bracket advances on each TP hit; SL never retreats.
        ''' Time filter: 11:00–17:00 UTC (London + NY pre-market, 07:00–13:00 EST/EDT).
        ''' </summary>
        Private Sub ApplyLultDivergence()
            InitialTpAmount = 20D
            InitialSlAmount = 10D
            Leverage = 5

            _currentStrategy = New StrategyDefinition With {
                .Name = "LULT Divergence",
                .Indicator = StrategyIndicatorType.LultDivergence,
                .Condition = StrategyConditionType.LultDivergence,
                .IndicatorPeriod = 100,
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 5,
                .DurationHours = 8760,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = 1,
                .InitialTpAmount = _initialTpAmount,
                .InitialSlAmount = _initialSlAmount,
                .Leverage = _leverage,
                .ScaleInAmount = 0D,
                .ScaleInLeverage = 1,
                .MinConfidencePct = _minConfidencePct
            }

            HasParsedStrategy = True
            ActiveStrategyText = "✔  LULT Divergence  (5-min · 24/7 · WaveTrend Anchor/Trigger · Engulfing · 2R)"
            ActiveStrategyKind = "Other"

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account + risk settings above, then click  ▶ Start Monitoring.")
            LogLine("• SL = trigger wave extreme ± 3 ticks · TP = 2R · Partial TP at nearest swing (50 %)")
            LogLine("• 6-step gate: Anchor→Trigger (shallower)→Divergence→Dot→Engulfing candle")
            LogLine("• Time filter: 11:00–17:00 UTC (07:00–13:00 EST/EDT) — London + NY pre-market")
            LogLine("• 5 independent sessions — OIL · GOLD · NSDQ100 · SPX500 · UK100")
            LogLine("━━━  LULT Divergence — Hydra 5-Asset Monitor  ━━━")
        End Sub

        ''' <summary>
        ''' Activates the BB Squeeze Scalper strategy for all 5 assets.
        ''' Dual-mode Bollinger Band scalper on 1-minute bars with 15-second polling.
        ''' DurationHours = 8 760 so sessions never auto-expire.
        ''' </summary>
        Private Sub ApplyBbSqueezeScalper()
            InitialTpAmount = 8D
            InitialSlAmount = 4D
            Leverage = 5

            _currentStrategy = New StrategyDefinition With {
                .Name = "BB Squeeze Scalper",
                .Indicator = StrategyIndicatorType.BbSqueezeScalper,
                .Condition = StrategyConditionType.BbSqueezeScalper,
                .IndicatorPeriod = 25,
                .SecondaryPeriod = 0,
                .IndicatorMultiplier = 2.0,
                .GoLongWhenBelowBands = True,
                .GoShortWhenAboveBands = True,
                .TimeframeMinutes = 1,
                .DurationHours = 8760,
                .CapitalAtRisk = _capitalAtRisk,
                .Quantity = 1,
                .InitialTpAmount = _initialTpAmount,
                .InitialSlAmount = _initialSlAmount,
                .Leverage = _leverage,
                .ScaleInAmount = 0D,
                .ScaleInLeverage = 1,
                .MinConfidencePct = _minConfidencePct
            }

            HasParsedStrategy = True
            ActiveStrategyText = "✔  BB Squeeze Scalper  (1-min · 24/7 · BB12 · %B · RSI7 · EMA5)"
            ActiveStrategyKind = "Other"

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account + risk settings above, then click  ▶ Start Monitoring.")
            LogLine($"• 1-min bars · TP=${_initialTpAmount:F0} · SL=${_initialSlAmount:F0} · Amt=${_capitalAtRisk:F0} · Lvg={_leverage}× · 15s polling")
            LogLine("• Mode B (Band Bounce): %B < 0 or > 1 + RSI7 extreme + rejection wick ≥ 60%")
            LogLine("• Mode A (Squeeze Breakout): BBW < SMA(BBW,20) ≥3 bars + band break + EMA5 + RSI7")
            LogLine("• 5 independent sessions — OIL · GOLD · NSDQ100 · SPX500 · UK100")
            LogLine("━━━  BB Squeeze Scalper — Hydra 5-Asset Monitor  ━━━")
        End Sub

        ' ── Command handlers ──────────────────────────────────────────────────────

        Private Sub ExecuteStart(param As Object)
            If _currentStrategy Is Nothing OrElse SelectedAccount Is Nothing Then Return

            IsRunning = True
            StatusText = $"● Running — {_currentStrategy.Name}"
            LogEntries.Clear()

            For i = 0 To 4
                Dim assetVm = Assets(i)
                ' Deep-copy the shared template so each engine has its own independent state.
                Dim sd As New StrategyDefinition With {
                    .Name = _currentStrategy.Name,
                    .Indicator = _currentStrategy.Indicator,
                    .Condition = _currentStrategy.Condition,
                    .IndicatorPeriod = _currentStrategy.IndicatorPeriod,
                    .SecondaryPeriod = _currentStrategy.SecondaryPeriod,
                    .IndicatorMultiplier = _currentStrategy.IndicatorMultiplier,
                    .GoLongWhenBelowBands = _currentStrategy.GoLongWhenBelowBands,
                    .GoShortWhenAboveBands = _currentStrategy.GoShortWhenAboveBands,
                    .TimeframeMinutes = _currentStrategy.TimeframeMinutes,
                    .DurationHours = _currentStrategy.DurationHours,
                    .ContractId = assetVm.ContractId,
                    .AccountId = SelectedAccount.Id,
                    .CapitalAtRisk = _capitalAtRisk,
                    .Quantity = 1,
                    .InitialTpAmount = _initialTpAmount,
                    .InitialSlAmount = _initialSlAmount,
                    .Leverage = _leverage,
                    .ScaleInAmount = _currentStrategy.ScaleInAmount,
                    .ScaleInLeverage = _currentStrategy.ScaleInLeverage,
                    .MinConfidencePct = _minConfidencePct
                }
                _engines(i).Start(sd)
                LogLine($"[{assetVm.Symbol}] Session started")
            Next
        End Sub

        Private Sub ExecuteStop(param As Object)
            For i = 0 To 4
                _engines(i).[Stop]()
            Next
            IsRunning = False
            StatusText = "● Not running"
        End Sub

        ' ── Helpers ───────────────────────────────────────────────────────────────

        Private Sub LogLine(message As String)
            LogEntries.Insert(0, message)
            Do While LogEntries.Count > 500
                LogEntries.RemoveAt(LogEntries.Count - 1)
            Loop
        End Sub

        Private Sub Dispatch(action As Action)
            If Application.Current?.Dispatcher IsNot Nothing Then
                Application.Current.Dispatcher.Invoke(action)
            End If
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                For i = 0 To 4
                    Try
                        _engines(i).Dispose()
                    Catch
                    End Try
                    Try
                        _assetScopes(i).Dispose()
                    Catch
                    End Try
                Next
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
