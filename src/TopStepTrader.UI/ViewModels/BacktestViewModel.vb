Imports System.Collections.ObjectModel
Imports System.IO
Imports System.Text
Imports System.Threading
Imports System.Windows
Imports Microsoft.Win32
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Trading
Imports TopStepTrader.UI.ViewModels.Base

Namespace TopStepTrader.UI.ViewModels

    ''' <summary>
    ''' Backtest page ViewModel — TICKET-006 Phase 2 update.
    '''
    ''' Workflow implemented:
    '''   1. User selects Contract  → resets strategy / clears results
    '''   2. User selects Strategy  → auto-adjusts Capital/Qty/TP/SL + starts bar download
    '''   3. Bar download completes → enables "Run Backtest" button
    '''   4. User clicks Run        → optionally trains EMA/RSI, then executes backtest
    '''   5. Results displayed      → metrics summary + trade list
    '''
    ''' Phase 2: DownloadBarsAsync calls IBarCollectionService.EnsureBarsAsync() (real download).
    ''' Phase 3: EMA/RSI training wired via IModelTrainingService.RetrainAsync().
    '''          Only combined multi-indicator strategies listed (TICKET-006 design decision).
    ''' Phase 4: CSV export implemented via SaveFileDialog + File.WriteAllText.
    ''' </summary>
    Public Class BacktestViewModel
        Inherits ViewModelBase

        Private ReadOnly _backtestService As IBacktestService
        Private ReadOnly _trainingService As IModelTrainingService
        Private ReadOnly _barCollectionService As IBarCollectionService

        Private _cancelSource As CancellationTokenSource

        ' Strategy parameter defaults are defined in StrategyDefaults (TopStepTrader.Core.Trading).
        ' Only combined multi-indicator strategies are listed — single-indicator strategies excluded by design.

        ' ══════════════════════════════════════════════════════════════════════
        ' CONFIGURATION PROPERTIES
        ' ══════════════════════════════════════════════════════════════════════

        Private _contractIdText As String = ""
        ''' <summary>
        ''' Long-form contract ID (e.g. "CON.F.US.MES.H26") — set by ContractSelectorControl.
        ''' Changing contract resets strategy selection, clears bars status and results.
        ''' </summary>
        Public Property ContractIdText As String
            Get
                Return _contractIdText
            End Get
            Set(value As String)
                Dim old = _contractIdText
                SetProperty(_contractIdText, value)
                If old <> value Then
                    ' Step 1 of workflow: reset all downstream state
                    _selectedStrategyName = Nothing
                    OnPropertyChanged(NameOf(SelectedStrategyName))
                    BarsAvailable = False
                    BarsStatusText = ""
                    HasBarsStatus = False
                    ClearResults()
                End If
            End Set
        End Property

        Private _selectedStrategyName As String
        ''' <summary>
        ''' Strategy chosen from the dropdown.
        ''' On change: auto-applies parameter defaults and triggers bar download.
        ''' </summary>
        Public Property SelectedStrategyName As String
            Get
                Return _selectedStrategyName
            End Get
            Set(value As String)
                Dim old = _selectedStrategyName
                SetProperty(_selectedStrategyName, value)
                If old <> value AndAlso Not String.IsNullOrEmpty(value) Then
                    ' Step 2a: auto-populate Capital/Qty/TP/SL from strategy optimums
                    ApplyStrategyDefaults(value)
                    ' Step 2b: trigger bar availability check + download (if contract selected)
                    If Not String.IsNullOrEmpty(_contractIdText) Then
                        DownloadBarsAsync()
                    End If
                End If
            End Set
        End Property

        Private _startDate As Date = DateTime.Today.AddMonths(-3)
        Public Property StartDate As Date
            Get
                Return _startDate
            End Get
            Set(value As Date)
                SetProperty(_startDate, value)
            End Set
        End Property

        Private _endDate As Date = DateTime.Today
        Public Property EndDate As Date
            Get
                Return _endDate
            End Get
            Set(value As Date)
                SetProperty(_endDate, value)
            End Set
        End Property

        Private _initialCapital As String = "50000"
        Public Property InitialCapital As String
            Get
                Return _initialCapital
            End Get
            Set(value As String)
                SetProperty(_initialCapital, value)
            End Set
        End Property

        Private _quantity As String = "1"
        ''' <summary>Number of contracts per signal. Auto-populated by strategy defaults.</summary>
        Public Property Quantity As String
            Get
                Return _quantity
            End Get
            Set(value As String)
                SetProperty(_quantity, value)
            End Set
        End Property

        Private _stopLossTicks As String = "10"
        Public Property StopLossTicks As String
            Get
                Return _stopLossTicks
            End Get
            Set(value As String)
                SetProperty(_stopLossTicks, value)
            End Set
        End Property

        Private _takeProfitTicks As String = "20"
        Public Property TakeProfitTicks As String
            Get
                Return _takeProfitTicks
            End Get
            Set(value As String)
                SetProperty(_takeProfitTicks, value)
            End Set
        End Property

        Private _minConfidence As String = "0.65"
        Public Property MinConfidence As String
            Get
                Return _minConfidence
            End Get
            Set(value As String)
                SetProperty(_minConfidence, value)
            End Set
        End Property

        Private _selectedInterval As String = "5 min"
        ''' <summary>
        ''' Selected bar timeframe for backtest (display format: "1 min", "5 min", etc.).
        ''' Changing the interval resets bars status and triggers a new bar check/download
        ''' (since 5-min bars and 15-min bars are stored separately in SQLite).
        ''' </summary>
        Public Property SelectedInterval As String
            Get
                Return _selectedInterval
            End Get
            Set(value As String)
                Dim old = _selectedInterval
                SetProperty(_selectedInterval, value)
                If old <> value AndAlso
                   Not String.IsNullOrEmpty(_contractIdText) AndAlso
                   Not String.IsNullOrEmpty(_selectedStrategyName) Then
                    BarsAvailable = False
                    BarsStatusText = ""
                    HasBarsStatus = False
                    DownloadBarsAsync()
                End If
            End Set
        End Property

        ' ══════════════════════════════════════════════════════════════════════
        ' PROGRESS / STATE PROPERTIES
        ' ══════════════════════════════════════════════════════════════════════

        Private _isRunning As Boolean
        Public Property IsRunning As Boolean
            Get
                Return _isRunning
            End Get
            Set(value As Boolean)
                SetProperty(_isRunning, value)
                OnPropertyChanged(NameOf(CanRun))
                OnPropertyChanged(NameOf(CanCancel))
                OnPropertyChanged(NameOf(CanTrain))
                OnPropertyChanged(NameOf(IsWorking))
                OnPropertyChanged(NameOf(IsIndeterminateProgress))
            End Set
        End Property

        Private _isTraining As Boolean
        Public Property IsTraining As Boolean
            Get
                Return _isTraining
            End Get
            Set(value As Boolean)
                SetProperty(_isTraining, value)
                OnPropertyChanged(NameOf(CanTrain))
                OnPropertyChanged(NameOf(CanRun))
                OnPropertyChanged(NameOf(IsWorking))
                OnPropertyChanged(NameOf(IsIndeterminateProgress))
            End Set
        End Property

        Private _isBarsDownloading As Boolean
        ''' <summary>True while the bar availability check / download is in progress.</summary>
        Public Property IsBarsDownloading As Boolean
            Get
                Return _isBarsDownloading
            End Get
            Set(value As Boolean)
                SetProperty(_isBarsDownloading, value)
                OnPropertyChanged(NameOf(IsWorking))
                OnPropertyChanged(NameOf(IsIndeterminateProgress))
            End Set
        End Property

        Private _barsAvailable As Boolean
        ''' <summary>
        ''' True when bars for the selected contract are confirmed available.
        ''' Controls whether "Run Backtest" is enabled (CanRun checks this).
        ''' </summary>
        Public Property BarsAvailable As Boolean
            Get
                Return _barsAvailable
            End Get
            Set(value As Boolean)
                SetProperty(_barsAvailable, value)
                OnPropertyChanged(NameOf(CanRun))
            End Set
        End Property

        Private _barsStatusText As String = ""
        ''' <summary>Human-readable bar-download status shown below the config form.</summary>
        Public Property BarsStatusText As String
            Get
                Return _barsStatusText
            End Get
            Set(value As String)
                SetProperty(_barsStatusText, value)
            End Set
        End Property

        Private _barsStatusColor As String = "AccentBrush"
        ''' <summary>BrushKeyConverter key: "AccentBrush" = neutral, "BuyBrush" = ok, "SellBrush" = error.</summary>
        Public Property BarsStatusColor As String
            Get
                Return _barsStatusColor
            End Get
            Set(value As String)
                SetProperty(_barsStatusColor, value)
            End Set
        End Property

        Private _hasBarsStatus As Boolean
        ''' <summary>Controls Visibility of the BarsStatusText TextBlock.</summary>
        Public Property HasBarsStatus As Boolean
            Get
                Return _hasBarsStatus
            End Get
            Set(value As Boolean)
                SetProperty(_hasBarsStatus, value)
            End Set
        End Property

        Private _progress As Integer
        Public Property Progress As Integer
            Get
                Return _progress
            End Get
            Set(value As Integer)
                SetProperty(_progress, value)
            End Set
        End Property

        Private _progressText As String = "Ready"
        Public Property ProgressText As String
            Get
                Return _progressText
            End Get
            Set(value As String)
                SetProperty(_progressText, value)
            End Set
        End Property

        ''' <summary>True when any async work is active — drives progress bar Visibility.</summary>
        Public ReadOnly Property IsWorking As Boolean
            Get
                Return _isRunning OrElse _isTraining OrElse _isBarsDownloading
            End Get
        End Property

        ''' <summary>
        ''' True during bar download or model training (indeterminate progress);
        ''' False during backtest execution (has a deterministic 0–100 value).
        ''' </summary>
        Public ReadOnly Property IsIndeterminateProgress As Boolean
            Get
                Return _isTraining OrElse _isBarsDownloading
            End Get
        End Property

        ''' <summary>
        ''' Run Backtest is enabled only when not already running/training AND bars are confirmed available.
        ''' This enforces the workflow: Contract → Strategy → Download → Run.
        ''' </summary>
        Public ReadOnly Property CanRun As Boolean
            Get
                Return Not _isRunning AndAlso Not _isTraining AndAlso _barsAvailable
            End Get
        End Property

        Public ReadOnly Property CanCancel As Boolean
            Get
                Return _isRunning
            End Get
        End Property

        Public ReadOnly Property CanTrain As Boolean
            Get
                Return Not _isRunning AndAlso Not _isTraining
            End Get
        End Property

        ' ══════════════════════════════════════════════════════════════════════
        ' RESULTS PROPERTIES
        ' ══════════════════════════════════════════════════════════════════════

        Public ReadOnly Property Trades As New ObservableCollection(Of BacktestTradeRowVm)()

        Private _totalTrades As Integer
        Public Property TotalTrades As Integer
            Get
                Return _totalTrades
            End Get
            Set(value As Integer)
                SetProperty(_totalTrades, value)
            End Set
        End Property

        Private _winRate As String = "—"
        Public Property WinRate As String
            Get
                Return _winRate
            End Get
            Set(value As String)
                SetProperty(_winRate, value)
            End Set
        End Property

        Private _totalPnL As Decimal
        Public Property TotalPnL As Decimal
            Get
                Return _totalPnL
            End Get
            Set(value As Decimal)
                SetProperty(_totalPnL, value)
                OnPropertyChanged(NameOf(PnLColor))
            End Set
        End Property

        Private _sharpe As String = "—"
        Public Property Sharpe As String
            Get
                Return _sharpe
            End Get
            Set(value As String)
                SetProperty(_sharpe, value)
            End Set
        End Property

        Private _maxDrawdown As Decimal
        Public Property MaxDrawdown As Decimal
            Get
                Return _maxDrawdown
            End Get
            Set(value As Decimal)
                SetProperty(_maxDrawdown, value)
            End Set
        End Property

        Private _avgPnL As Decimal
        Public Property AvgPnL As Decimal
            Get
                Return _avgPnL
            End Get
            Set(value As Decimal)
                SetProperty(_avgPnL, value)
            End Set
        End Property

        Public ReadOnly Property PnLColor As String
            Get
                Return If(_totalPnL >= 0, "BuyBrush", "SellBrush")
            End Get
        End Property

        ' ══════════════════════════════════════════════════════════════════════
        ' COLLECTIONS
        ' ══════════════════════════════════════════════════════════════════════

        Public ReadOnly Property PreviousRuns As New ObservableCollection(Of BacktestRunSummaryVm)()

        ''' <summary>
        ''' Combined multi-indicator strategies shown in the dropdown.
        ''' Single-indicator strategies (pure RSI, pure EMA, etc.) are excluded by design —
        ''' backtesting a single-indicator strategy does not produce reliable live trading signals.
        ''' </summary>
        Public ReadOnly Property AvailableStrategies As New ObservableCollection(Of String)()

        ''' <summary>
        ''' Bar timeframe options for backtest (display format: "1 min", "5 min", etc.).
        ''' Currently 5-minute bars are cached; other intervals are for future use.
        ''' </summary>
        Public ReadOnly Property AvailableIntervals As New ObservableCollection(Of String)()

        ''' <summary>
        ''' Legacy collection — retained for compatibility; not bound in the new XAML.
        ''' ContractSelectorControl is self-contained and does not use this list.
        ''' </summary>
        Public ReadOnly Property AvailableContracts As New ObservableCollection(Of Contract)()

        ' ══════════════════════════════════════════════════════════════════════
        ' COMMANDS
        ' ══════════════════════════════════════════════════════════════════════

        Public ReadOnly Property RunCommand As RelayCommand
        Public ReadOnly Property CancelCommand As RelayCommand
        Public ReadOnly Property LoadHistoryCommand As RelayCommand
        Public ReadOnly Property TrainModelCommand As RelayCommand
        Public ReadOnly Property ExportCsvCommand As RelayCommand

        ' ══════════════════════════════════════════════════════════════════════
        ' CONSTRUCTOR
        ' ══════════════════════════════════════════════════════════════════════

        Public Sub New(backtestService As IBacktestService,
                       trainingService As IModelTrainingService,
                       barCollectionService As IBarCollectionService)

            _backtestService = backtestService
            _trainingService = trainingService
            _barCollectionService = barCollectionService

            ' Populate legacy contract collection (not used by new XAML)
            For Each f In FavouriteContracts.GetDefaults()
                AvailableContracts.Add(New Contract With {
                    .Id = f.ContractId,
                    .FriendlyName = f.Name
                })
            Next

            ' Populate strategy dropdown — combined strategies only.
            ' Single-indicator strategies excluded per TICKET-006 design decision.
            AvailableStrategies.Add("EMA/RSI Combined")

            ' Populate interval dropdown — only natively-supported ProjectX API timeframes.
            ' Removed: "3 min" (API falls back to 1-min), "10 min" (no API code), "4 hours" (no API code).
            ' Added:   "30 min" (API unit=4, fully supported).
            AvailableIntervals.Add("1 min")
            AvailableIntervals.Add("5 min")
            AvailableIntervals.Add("15 min")
            AvailableIntervals.Add("30 min")
            AvailableIntervals.Add("1 hour")

            RunCommand = New RelayCommand(AddressOf ExecuteRun, Function() CanRun)
            CancelCommand = New RelayCommand(AddressOf ExecuteCancel, Function() CanCancel)
            LoadHistoryCommand = New RelayCommand(AddressOf LoadPreviousRuns)
            TrainModelCommand = New RelayCommand(AddressOf ExecuteTrainModel, Function() CanTrain)
            ExportCsvCommand = New RelayCommand(AddressOf ExecuteExportCsv)

            AddHandler _backtestService.ProgressUpdated, AddressOf OnProgress
        End Sub

        Public Sub LoadDataAsync()
            LoadPreviousRuns()
        End Sub

        ' ══════════════════════════════════════════════════════════════════════
        ' STRATEGY WORKFLOW — Steps 2a, 2b, 3
        ' ══════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Step 2a: Auto-populate Capital/Qty/TP/SL from the strategy's optimum defaults.
        ''' Delegates to <see cref="StrategyDefaults.TryGet"/> in TopStepTrader.Core.
        ''' The user may override the values after auto-adjust.
        ''' Only combined multi-indicator strategies are registered — per TICKET-006 design.
        ''' </summary>
        Private Sub ApplyStrategyDefaults(strategyName As String)
            Dim defaults = StrategyDefaults.TryGet(strategyName)
            If defaults IsNot Nothing Then
                InitialCapital = defaults.Capital
                Quantity = defaults.Qty
                TakeProfitTicks = defaults.TakeProfitTicks
                StopLossTicks = defaults.StopLossTicks
            End If
        End Sub

        ''' <summary>
        ''' Step 2b / Step 3: Ensure 5-minute bars are cached in SQLite for the selected
        ''' contract and date range.  Calls BarCollectionService.EnsureBarsAsync() which:
        '''   - Returns immediately if ≥ 50 bars already exist (cache hit)
        '''   - Otherwise pages the ProjectX API in 500-bar batches and stores to SQLite
        '''   - Reports progress after each batch via BarsStatusText
        ''' Enables "Run Backtest" on success; disables with an error message on failure.
        ''' </summary>
        Private Sub DownloadBarsAsync()
            BarsAvailable = False
            IsBarsDownloading = True
            BarsStatusText = $"⏳ Checking {_selectedInterval} bars for {_contractIdText}..."
            BarsStatusColor = "AccentBrush"
            HasBarsStatus = True

            ' Capture mutable fields so the background closure uses their values at call time
            Dim contractId = _contractIdText
            Dim fromDate = _startDate
            Dim toDate = _endDate
            Dim timeframe = ParseIntervalToTimeframe(_selectedInterval)

            Task.Run(Async Function()
                         Try
                             ' Progress reporter — marshals each status string to the UI thread
                             Dim prog = New Progress(Of String)(
                                 Sub(msg) Dispatch(Sub()
                                                       BarsStatusText = msg
                                                       BarsStatusColor = If(msg.StartsWith("✓"), "BuyBrush",
                                                                         If(msg.StartsWith("✗"), "SellBrush",
                                                                            "AccentBrush"))
                                                   End Sub))

                             Dim cts = New CancellationTokenSource(TimeSpan.FromMinutes(5))
                             Dim result = Await _barCollectionService.EnsureBarsAsync(
                                              contractId, fromDate, toDate, timeframe, prog, cts.Token)

                             Dispatch(Sub()
                                          IsBarsDownloading = False
                                          BarsAvailable = result.Success
                                          BarsStatusText = result.Message
                                          BarsStatusColor = If(result.Success, "BuyBrush", "SellBrush")
                                      End Sub)

                         Catch ex As OperationCanceledException
                             Dispatch(Sub()
                                          IsBarsDownloading = False
                                          BarsAvailable = False
                                          BarsStatusText = "✗ Bar download timed out (> 5 min)"
                                          BarsStatusColor = "SellBrush"
                                      End Sub)
                         Catch ex As Exception
                             Dispatch(Sub()
                                          IsBarsDownloading = False
                                          BarsAvailable = False
                                          BarsStatusText = $"✗ Bar download failed: {ex.Message}"
                                          BarsStatusColor = "SellBrush"
                                      End Sub)
                         End Try
                     End Function)
        End Sub

        ''' <summary>Reset all results to empty/default state (called on contract change).</summary>
        Private Sub ClearResults()
            TotalTrades = 0
            WinRate = "—"
            TotalPnL = 0
            MaxDrawdown = 0
            Sharpe = "—"
            AvgPnL = 0
            Trades.Clear()
            ProgressText = "Ready"
            Progress = 0
        End Sub

        ' ══════════════════════════════════════════════════════════════════════
        ' TRAIN MODEL — Step 4 (optional, ML-based strategies only)
        ' ══════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Pre-calculates EMA/RSI indicator values on the downloaded bars.
        ''' Rule-based strategies (RSI Reversal, Double Bottom, etc.) do not need training.
        ''' The "Train Model" button is always available for the user to force a retrain.
        '''
        ''' UAT-BUG-004: Pass the user-selected ContractId so ModelTrainingService fetches bars
        ''' for the same contract the user downloaded, not TradingSettings.ActiveContractIds
        ''' (which is the live-trading config and may be empty or contain different contracts).
        ''' </summary>
        Private Sub ExecuteTrainModel()
            IsTraining = True
            ProgressText = "Training ML model on DB bars... (may take 30–60 s)"
            Progress = 0

            ' Capture the selected contract at call time so the background closure uses the
            ' correct value even if the UI changes after the task is dispatched.
            ' Pass Nothing when no contract is selected — service falls back to ActiveContractIds.
            Dim trainingContractId As String =
                If(Not String.IsNullOrWhiteSpace(_contractIdText), _contractIdText.Trim(), Nothing)

            Task.Run(Async Function()
                         Try
                             Dim cts = New CancellationTokenSource(TimeSpan.FromMinutes(5))
                             Dim metrics = Await _trainingService.RetrainAsync(cts.Token, trainingContractId)
                             Dispatch(Sub()
                                          If metrics IsNot Nothing Then
                                              ProgressText = $"✓ Model trained — Acc: {metrics.Accuracy:P1}  AUC: {metrics.AUC:F3}  F1: {metrics.F1Score:F3}  Samples: {metrics.TrainingSamples}"
                                          Else
                                              ProgressText = "Training skipped — insufficient bar data (need ≥ 200 bars)"
                                          End If
                                          Progress = 100
                                      End Sub)
                         Catch ex As Exception
                             Dispatch(Sub() ProgressText = $"✗ Training error: {ex.Message}")
                         Finally
                             Dispatch(Sub()
                                          IsTraining = False
                                          ' Explicitly invalidate command state to ensure Run Backtest button updates
                                          RelayCommand.RaiseCanExecuteChanged()
                                      End Sub)
                         End Try
                     End Function)
        End Sub

        ' ══════════════════════════════════════════════════════════════════════
        ' RUN BACKTEST — Steps 5, 6, 7
        ' ══════════════════════════════════════════════════════════════════════

        Private Sub ExecuteRun()
            Dim contractId = _contractIdText.Trim()
            If String.IsNullOrEmpty(contractId) Then
                ProgressText = "Select a contract first" : Return
            End If

            If String.IsNullOrEmpty(_selectedStrategyName) Then
                ProgressText = "Select a strategy first" : Return
            End If

            Dim capital As Decimal
            If Not Decimal.TryParse(_initialCapital, capital) OrElse capital <= 0 Then
                ProgressText = "Invalid initial capital" : Return
            End If

            Dim slTicks, tpTicks, qty As Integer
            Integer.TryParse(_stopLossTicks, slTicks)
            Integer.TryParse(_takeProfitTicks, tpTicks)
            Integer.TryParse(_quantity, qty)

            Dim conf As Single
            Single.TryParse(_minConfidence, conf)

            Dim config As New BacktestConfiguration With {
                .RunName = $"Backtest {DateTime.Now:yyyyMMdd-HHmm} — {_selectedStrategyName} ({_selectedInterval})",
                .ContractId = contractId,
                .Timeframe = CInt(ParseIntervalToTimeframe(_selectedInterval)),
                .StartDate = _startDate,
                .EndDate = _endDate,
                .InitialCapital = capital,
                .StopLossTicks = If(slTicks > 0, slTicks, 20),
                .TakeProfitTicks = If(tpTicks > 0, tpTicks, 40),
                .MinSignalConfidence = If(conf > 0, conf, 0.65F),
                .Quantity = If(qty > 0, qty, 1),
                .TickSize = GetTickSize(contractId),
                .PointValue = GetPointValue(contractId)
            }

            _cancelSource = New CancellationTokenSource()
            IsRunning = True
            Progress = 0
            ClearResults()
            ProgressText = $"Running — {_selectedStrategyName} on {contractId}..."

            Task.Run(Async Function()
                         Try
                             Dim result = Await _backtestService.RunBacktestAsync(config, _cancelSource.Token)
                             Dispatch(Sub() ShowResult(result))
                         Catch ex As OperationCanceledException
                             Dispatch(Sub() ProgressText = "Backtest cancelled")
                         Catch ex As Exception
                             Dispatch(Sub() ProgressText = $"Error: {ex.Message}")
                         Finally
                             Dispatch(Sub()
                                          IsRunning = False
                                          _cancelSource?.Dispose()
                                      End Sub)
                         End Try
                     End Function)
        End Sub

        ''' <summary>
        ''' Converts the display-format interval string (e.g. "5 min", "1 hour") to the
        ''' <see cref="BarTimeframe"/> enum value used by BarCollectionService and BacktestEngine.
        ''' Falls back to FiveMinute for any unrecognised string.
        ''' </summary>
        Private Shared Function ParseIntervalToTimeframe(interval As String) As BarTimeframe
            Select Case interval
                Case "1 min" : Return BarTimeframe.OneMinute
                Case "5 min" : Return BarTimeframe.FiveMinute
                Case "15 min" : Return BarTimeframe.FifteenMinute
                Case "30 min" : Return BarTimeframe.ThirtyMinute
                Case "1 hour" : Return BarTimeframe.OneHour
                Case Else : Return BarTimeframe.FiveMinute
            End Select
        End Function

        ''' <summary>
        ''' Returns the price-units-per-tick for the given contract.
        ''' Used to convert tick counts (SL/TP) into exact price levels in BacktestMetrics.
        ''' MES/MNQ: 0.25 (quarter-point ticks)
        ''' MGC (Micro Gold): 0.10 (dime ticks)
        ''' MCL (Micro Crude): 0.01 (cent ticks)
        ''' </summary>
        Private Shared Function GetTickSize(contractId As String) As Decimal
            If contractId.Contains("MGC") Then Return 0.10D
            If contractId.Contains("MCL") Then Return 0.01D
            Return 0.25D   ' MES, MNQ default
        End Function

        ''' <summary>
        ''' Returns the dollar-per-point value for the given contract.
        ''' Used by BacktestMetrics.CalculatePnL: P&amp;L = priceDiff × qty × pointValue.
        ''' MES:  $5 /point (was wrongly $50 — that is the full-size ES)
        ''' MNQ:  $2 /point
        ''' MGC: $10 /point (Micro Gold, 10 troy oz)
        ''' MCL: $10 /point (Micro Crude, 10 barrels)
        ''' </summary>
        Private Shared Function GetPointValue(contractId As String) As Decimal
            If contractId.Contains("MGC") Then Return 10.0D
            If contractId.Contains("MCL") Then Return 100.0D
            If contractId.Contains("MNQ") Then Return 2.0D
            Return 5.0D   ' MES default
        End Function

        Private Sub ExecuteCancel(param As Object)
            _cancelSource?.Cancel()
        End Sub

        Private Sub ShowResult(result As BacktestResult)
            TotalTrades = result.TotalTrades
            WinRate = result.WinRate.ToString("P1")
            TotalPnL = result.TotalPnL
            MaxDrawdown = result.MaxDrawdown
            AvgPnL = result.AveragePnLPerTrade
            Sharpe = If(result.SharpeRatio.HasValue, result.SharpeRatio.Value.ToString("F2"), "—")
            ProgressText = $"Complete — {result.TotalTrades} trades · {result.WinRate:P0} win rate · {result.TotalPnL:C0} P&L"
            Progress = 100

            Trades.Clear()
            For Each t In result.Trades
                Trades.Add(New BacktestTradeRowVm(t))
            Next
        End Sub

        ' ══════════════════════════════════════════════════════════════════════
        ' EXPORT CSV — Phase 4
        ' ══════════════════════════════════════════════════════════════════════

        ''' <summary>
        ''' Exports backtest results to a CSV file chosen via SaveFileDialog.
        ''' Columns match the trade DataGrid: Entry Time, Exit Time, Side, Entry Price,
        ''' Exit Price, P&amp;L, Exit Reason, Confidence.
        ''' All values are double-quoted to handle commas in currency-formatted P&amp;L.
        ''' </summary>
        Private Sub ExecuteExportCsv()
            If Trades.Count = 0 Then
                ProgressText = "No results to export — run a backtest first"
                Return
            End If

            Dim dlg As New SaveFileDialog() With {
                .Title = "Export Backtest Results",
                .Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                .FileName = $"Backtest_{If(_selectedStrategyName, "Results")}_{DateTime.Now:yyyyMMdd-HHmm}.csv"
            }

            If dlg.ShowDialog() <> True Then Return

            Try
                Dim sb As New StringBuilder()
                sb.AppendLine("Entry Time,Exit Time,Side,Entry Price,Exit Price,P&L,Exit Reason,Confidence")
                For Each t In Trades
                    sb.AppendLine($"""{t.EntryTime}"",""{t.ExitTime}"",""{t.Side}""," &
                                  $"""{t.EntryPrice}"",""{t.ExitPrice}"",""{t.PnL}"",""{t.ExitReason}"",""{t.Confidence}""")
                Next
                File.WriteAllText(dlg.FileName, sb.ToString())
                ProgressText = $"✓ Exported {Trades.Count} trades → {Path.GetFileName(dlg.FileName)}"
            Catch ex As Exception
                ProgressText = $"✗ Export failed: {ex.Message}"
            End Try
        End Sub

        ' ══════════════════════════════════════════════════════════════════════
        ' PREVIOUS RUNS (Tab 2)
        ' ══════════════════════════════════════════════════════════════════════

        Private Sub LoadPreviousRuns()
            Task.Run(Async Function()
                         Try
                             Dim runs = Await _backtestService.GetBacktestRunsAsync()
                             Dispatch(Sub()
                                          PreviousRuns.Clear()
                                          For Each r In runs.OrderByDescending(Function(x) x.Id)
                                              PreviousRuns.Add(New BacktestRunSummaryVm(r))
                                          Next
                                      End Sub)
                         Catch
                             ' Silently ignore history load errors
                         End Try
                     End Function)
        End Sub

        ' ══════════════════════════════════════════════════════════════════════
        ' INFRASTRUCTURE
        ' ══════════════════════════════════════════════════════════════════════

        Private Sub OnProgress(sender As Object, e As BacktestProgressEventArgs)
            Dispatch(Sub()
                         Progress = e.PercentComplete
                         ProgressText = $"{e.PercentComplete}% — {e.CurrentDate:MM/dd/yyyy} — {e.TradesExecuted} trades"
                     End Sub)
        End Sub

        Private Sub Dispatch(action As Action)
            If Application.Current?.Dispatcher IsNot Nothing Then
                Application.Current.Dispatcher.Invoke(action)
            End If
        End Sub

    End Class

    ' ══════════════════════════════════════════════════════════════════════════
    ' ROW VIEW-MODELS (unchanged)
    ' ══════════════════════════════════════════════════════════════════════════

    Public Class BacktestTradeRowVm
        Public Property EntryTime As String
        Public Property ExitTime As String
        Public Property Side As String
        Public Property EntryPrice As String
        Public Property ExitPrice As String
        Public Property PnL As String
        Public Property ExitReason As String
        Public Property Confidence As String

        Public ReadOnly Property PnLColor As String
            Get
                Return If(PnL.StartsWith("-"), "SellBrush", "BuyBrush")
            End Get
        End Property

        Public Sub New(t As BacktestTrade)
            EntryTime = t.EntryTime.LocalDateTime.ToString("MM/dd HH:mm")
            ExitTime = If(t.ExitTime.HasValue, t.ExitTime.Value.LocalDateTime.ToString("MM/dd HH:mm"), "—")
            Side = t.Side
            EntryPrice = t.EntryPrice.ToString("F2")
            ExitPrice = If(t.ExitPrice.HasValue, t.ExitPrice.Value.ToString("F2"), "—")
            PnL = If(t.PnL.HasValue, t.PnL.Value.ToString("C0"), "—")
            ExitReason = t.ExitReason
            Confidence = t.SignalConfidence.ToString("P0")
        End Sub
    End Class

    Public Class BacktestRunSummaryVm
        Public Property Id As Long
        Public Property RunName As String
        Public Property StartDate As String
        Public Property EndDate As String
        Public Property Trades As Integer
        Public Property WinRate As String
        Public Property TotalPnL As String
        Public Property Sharpe As String

        Public Sub New(r As BacktestResult)
            Id = r.Id
            RunName = r.RunName
            StartDate = r.StartDate.ToString("MM/dd/yyyy")
            EndDate = r.EndDate.ToString("MM/dd/yyyy")
            Trades = r.TotalTrades
            WinRate = r.WinRate.ToString("P1")
            TotalPnL = r.TotalPnL.ToString("C0")
            Sharpe = If(r.SharpeRatio.HasValue, r.SharpeRatio.Value.ToString("F2"), "—")
        End Sub
    End Class

End Namespace
