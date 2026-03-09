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
    ''' ViewModel for the CryptoJoe multi-asset monitoring view.
    ''' Runs 5 independent EMA/RSI Combined sessions (one per crypto asset) concurrently and
    ''' surfaces per-asset confidence snapshots in real time.  No session expiry — monitors 24/7.
    ''' Each engine runs in its own DI scope so BarIngestionService and IOrderService are
    ''' fully isolated between assets.
    ''' </summary>
    Public Class CryptoJoeViewModel
        Inherits ViewModelBase
        Implements IDisposable

        ' ── Asset roster (fixed 5 crypto instruments) ────────────────────────────
        Private Shared ReadOnly Symbols As String() = {"BTC", "ETH", "XRP", "SOL", "BNB"}
        Private Shared ReadOnly Icons As String() = {"₿", "Ξ", "✕", "◎", "◈"}

        ' ── Dependencies ──────────────────────────────────────────────────────────
        Private ReadOnly _scopeFactory As IServiceScopeFactory
        Private ReadOnly _accountService As IAccountService

        ' ── Per-asset scope + engine ──────────────────────────────────────────────
        Private ReadOnly _assetScopes(4) As IServiceScope
        Private ReadOnly _engines(4) As CryptoStrategyExecutionEngine

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

        Private _takeProfitPct As Decimal = 4.0D
        Public Property TakeProfitPct As Decimal
            Get
                Return _takeProfitPct
            End Get
            Set(value As Decimal)
                SetProperty(_takeProfitPct, Math.Max(0D, value))
            End Set
        End Property

        Private _stopLossPct As Decimal = 1.5D
        Public Property StopLossPct As Decimal
            Get
                Return _stopLossPct
            End Get
            Set(value As Decimal)
                SetProperty(_stopLossPct, Math.Max(0D, value))
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

        Public ReadOnly Property IsFormReady As Boolean
            Get
                Return _selectedAccount IsNot Nothing
            End Get
        End Property

        ' ── Status / Log ──────────────────────────────────────────────────────────
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

        ' ── Commands ──────────────────────────────────────────────────────────────
        Public ReadOnly Property SelectEmaRsiCombinedCommand As RelayCommand
        Public ReadOnly Property SelectMultiConfluenceEngineCommand As RelayCommand
        Public ReadOnly Property SelectLultDivergenceCommand As RelayCommand
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
                                  .GetRequiredService(Of CryptoStrategyExecutionEngine)()
                ' All crypto assets trade 24/7 — ordering is always allowed.
                _engines(i).IsOrderingAllowed = Function() True
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

            StartCommand = New RelayCommand(
                AddressOf ExecuteStart,
                Function(p) HasParsedStrategy AndAlso IsNotRunning AndAlso SelectedAccount IsNot Nothing)

            StopCommand = New RelayCommand(
                AddressOf ExecuteStop,
                Function(p) IsRunning)
        End Sub

        ' ── Engine event wiring ───────────────────────────────────────────────────

        Private Sub WireEngineEvents(engine As CryptoStrategyExecutionEngine,
                                     assetVm As HydraAssetViewModel)
            AddHandler engine.ConfidenceUpdated,
                Sub(s As Object, e As ConfidenceUpdatedEventArgs)
                    Dispatch(Sub() assetVm.ApplyConfidence(e.UpPct, e.DownPct, e.AdxGatePassed))
                End Sub

            AddHandler engine.LogMessage,
                Sub(s As Object, msg As String)
                    Dispatch(Sub() LogLine($"[{assetVm.Symbol}] {msg}"))
                End Sub

            AddHandler engine.ExecutionStopped,
                Sub(s As Object, reason As String)
                    Dispatch(Sub() LogLine($"[{assetVm.Symbol}] ■ Stopped: {reason}"))
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
            Catch ex As Exception
                Dispatch(Sub() StatusText = $"⚠ Load error: {ex.Message}")
            End Try
        End Sub

        ' ── Strategy activation ───────────────────────────────────────────────────

        ''' <summary>
        ''' Activates the EMA/RSI Combined strategy for all 5 crypto assets.
        ''' DurationHours = 8 760 (one calendar year) so sessions never auto-expire
        ''' — satisfying the "runs 24/7" requirement.
        ''' </summary>
        Private Sub ApplyEmaRsiCombined()
            TakeProfitPct = 4.0D
            StopLossPct = 1.5D
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
                .TakeProfitPct = _takeProfitPct,
                .StopLossPct = _stopLossPct,
                .Leverage = _leverage,
                .ScaleInAmount = 200D,
                .ScaleInLeverage = 5,
                .MinConfidencePct = _minConfidencePct
            }

            HasParsedStrategy = True
            ActiveStrategyText = "✔  EMA/RSI Combined  (5-min · 24/7 · EMA21/EMA50/RSI14)"

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account + risk settings above, then click  ▶ Start Monitoring.")
            LogLine($"• 5-min bars · TP={_takeProfitPct:F1}% · SL={_stopLossPct:F1}% · Amt=${_capitalAtRisk:F0} · Lvg={_leverage}× · Confidence={_minConfidencePct}%")
            LogLine("• 5 independent sessions — BTC · ETH · XRP · SOL · BNB")
            LogLine("━━━  EMA/RSI Combined — CryptoJoe 5-Asset Monitor  ━━━")
        End Sub

        ''' <summary>
        ''' Activates the Multi-Confluence Engine strategy for all 5 crypto assets.
        ''' Uses ATR-derived SL/TP (TakeProfitPct = 0, StopLossPct = 0 so the engine
        ''' computes absolute prices from 1.5×ATR / 3×ATR at entry time).
        ''' DurationHours = 8 760 so sessions never auto-expire.
        ''' </summary>
        Private Sub ApplyMultiConfluenceEngine()
            TakeProfitPct = 0D
            StopLossPct = 0D
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
                .TakeProfitPct = 0D,
                .StopLossPct = 0D,
                .Leverage = _leverage,
                .ScaleInAmount = 200D,
                .ScaleInLeverage = 5,
                .MinConfidencePct = _minConfidencePct
            }

            HasParsedStrategy = True
            ActiveStrategyText = "✔  Multi-Confluence Engine  (15-min · 24/7 · Ichimoku · EMA21/50 · MACD · StochRSI · ADX)"

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account + risk settings above, then click  ▶ Start Monitoring.")
            LogLine("\u2022 SL = min(1.5×ATR, cloud edge) · TP = 2:1 R:R (dynamic per-trade ATR)")
            LogLine("• Entry fires only when ALL 7 conditions align (Ichimoku + EMA21 + Tenkan/Kijun + Chikou + ADX + MACD + StochRSI)")
            LogLine("• 5 independent sessions — BTC · ETH · XRP · SOL · BNB")
            LogLine("━━━  Multi-Confluence Engine — CryptoJoe 5-Asset Monitor  ━━━")
        End Sub

        ''' <summary>
        ''' Activates the LULT Divergence strategy for all 5 crypto assets.
        ''' Uses WaveTrend (Market Cipher B) Anchor/Trigger divergence on 5-minute bars.
        ''' SL is computed from the trigger wave extreme ± ATR-scaled buffer at signal time;
        ''' TP = 2R.  TakeProfitPct = 0 and StopLossPct = 0 so the engine takes the
        ''' LULT-specific PlaceBracketOrdersAsync override path.
        ''' Time filter: 11:00–17:00 UTC (London + NY pre-market, 07:00–13:00 EST/EDT).
        ''' </summary>
        Private Sub ApplyLultDivergence()
            TakeProfitPct = 0D
            StopLossPct = 0D
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
                .TakeProfitPct = 0D,
                .StopLossPct = 0D,
                .Leverage = _leverage,
                .ScaleInAmount = 0D,
                .ScaleInLeverage = 1,
                .MinConfidencePct = _minConfidencePct
            }

            HasParsedStrategy = True
            ActiveStrategyText = "✔  LULT Divergence  (5-min · 24/7 · WaveTrend Anchor/Trigger · Engulfing · 2R)"

            LogEntries.Clear()
            LogLine("─────────────────────────────────────────────────────────────────────")
            LogLine("Configure account + risk settings above, then click  ▶ Start Monitoring.")
            LogLine("• SL = trigger wave extreme ± 3 ticks · TP = 2R · Partial TP at nearest swing (50 %)")
            LogLine("• 6-step gate: Anchor→Trigger (shallower)→Divergence→Dot→Engulfing candle")
            LogLine("• Time filter: 11:00–17:00 UTC (07:00–13:00 EST/EDT) — London + NY pre-market")
            LogLine("• 5 independent sessions — BTC · ETH · XRP · SOL · BNB")
            LogLine("━━━  LULT Divergence — CryptoJoe 5-Asset Monitor  ━━━")
        End Sub

        ' ── Command handlers ──────────────────────────────────────────────────────

        Private Sub ExecuteStart(param As Object)
            If _currentStrategy Is Nothing OrElse SelectedAccount Is Nothing Then Return

            IsRunning = True
            StatusText = "● Running — 🪙 CryptoJoe | BTC · ETH · XRP · SOL · BNB"
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
                    .TakeProfitPct = _takeProfitPct,
                    .StopLossPct = _stopLossPct,
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
            StatusText = "● Idle"
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
