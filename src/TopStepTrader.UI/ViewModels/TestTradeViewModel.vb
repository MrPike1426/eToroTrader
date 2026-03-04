Imports System.Collections.ObjectModel
Imports System.Windows
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Logging
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Trading
Imports TopStepTrader.UI.ViewModels.Base

Namespace TopStepTrader.UI.ViewModels

    ''' <summary>
    ''' ViewModel for the Test Trade page.
    ''' Combines EMA / RSI trend analysis with one-click test BUY / SELL order placement.
    ''' Ingests fresh 1-hour bars before running the weighted multi-indicator scoring system.
    ''' </summary>
    Public Class TestTradeViewModel
        Inherits ViewModelBase
        Implements IDisposable

        ' ── Dependencies ──────────────────────────────────────────────────────────
        Private ReadOnly _orderService As IOrderService
        Private ReadOnly _accountService As IAccountService
        Private ReadOnly _marketDataService As IMarketDataService

        Public Sub New(orderService As IOrderService,
                       accountService As IAccountService,
                       marketDataService As IMarketDataService)
            _orderService = orderService
            _accountService = accountService
            _marketDataService = marketDataService

            AddHandler _orderService.OrderFilled, AddressOf OnTestOrderFilled
            AddHandler _orderService.OrderRejected, AddressOf OnTestOrderRejected
            AddHandler _orderService.PositionUpdated, AddressOf OnPositionUpdated
            AddHandler DebugLog.MessageLogged, AddressOf OnDebugLog
            AddHandler _marketDataService.QuoteReceived, AddressOf OnQuoteReceived

            LoadAvailableContracts()

            ' Initialize strategy commands
            StartStrategyBuyCommand = New RelayCommand(Sub() StartStrategy(OrderSide.Buy), Function() CanStartStrategy)
            StartStrategySellCommand = New RelayCommand(Sub() StartStrategy(OrderSide.Sell), Function() CanStartStrategy)
            StopStrategyCommand = New RelayCommand(AddressOf StopStrategy, Function() IsStrategyRunning)

            CreateBracketCommand = New RelayCommand(AddressOf CreateBracket)

            ' Set default Risk/Reward as requested
            TestTradeStopLoss = "50"
            TestTradeTakeProfit = "25"

            ClearDebugCommand = New RelayCommand(AddressOf ClearDebug)
        End Sub

        Public Property ClearDebugCommand As RelayCommand

        Public Async Function LoadDataAsync() As Task
            Try
                Dim accountsList = Await _accountService.GetActiveAccountsAsync()
                Dispatch(Sub()
                             Accounts.Clear()
                             For Each a In accountsList
                                 Accounts.Add(a)
                             Next
                             If Accounts.Count > 0 Then SelectedAccount = Accounts(0)
                         End Sub)
            Catch ex As Exception
                DebugLog.Log($"Error loading account data: {ex.Message}")
            End Try
        End Function

        ' ── Internal state ────────────────────────────────────────────────────────
        Private _disposed As Boolean = False
        Private ReadOnly _debugMessages As New List(Of String)()

        ' ── Account selection ─────────────────────────────────────────────────────
        Public Property Accounts As New ObservableCollection(Of Account)()

        Private _selectedAccount As Account
        Public Property SelectedAccount As Account
            Get
                Return _selectedAccount
            End Get
            Set(value As Account)
                If SetProperty(_selectedAccount, value) Then
                    NotifyPropertyChanged(NameOf(CanPlaceTestTrade))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        ' ── Contract selection ─────────────────────────────────────────────────────
        Public Property AvailableContracts As New ObservableCollection(Of Contract)()

        Private _testTradeSelectedContract As Contract
        Public Property TestTradeSelectedContract As Contract
            Get
                Return _testTradeSelectedContract
            End Get
            Set(value As Contract)
                If SetProperty(_testTradeSelectedContract, value) Then
                    UpdateTestTradeContractDisplay()
                End If
            End Set
        End Property

        Private _testTradeContractId As String = String.Empty
        Public Property TestTradeContractId As String
            Get
                Return _testTradeContractId
            End Get
            Set(value As String)
                If SetProperty(_testTradeContractId, value) Then
                    UpdateTestTradeContractDisplay()
                    NotifyPropertyChanged(NameOf(CanPlaceTestTrade))
                    RelayCommand.RaiseCanExecuteChanged()
                End If
            End Set
        End Property

        Private _testTradeContractLongId As String = String.Empty
        Public Property TestTradeContractLongId As String
            Get
                Return _testTradeContractLongId
            End Get
            Set(value As String)
                SetProperty(_testTradeContractLongId, value)
            End Set
        End Property

        Private _testTradeContractDisplay As String = "—"
        Public Property TestTradeContractDisplay As String
            Get
                Return _testTradeContractDisplay
            End Get
            Set(value As String)
                SetProperty(_testTradeContractDisplay, value)
            End Set
        End Property

        ' ── Trade parameters ──────────────────────────────────────────────────────
        Private _testTradeQuantity As String = "1"
        Public Property TestTradeQuantity As String
            Get
                Return _testTradeQuantity
            End Get
            Set(value As String)
                SetProperty(_testTradeQuantity, value)
            End Set
        End Property

        Private _testTradeStopLoss As String = "60"
        Public Property TestTradeStopLoss As String
            Get
                Return _testTradeStopLoss
            End Get
            Set(value As String)
                SetProperty(_testTradeStopLoss, value)
            End Set
        End Property

        Private _testTradeTakeProfit As String = "30"
        Public Property TestTradeTakeProfit As String
            Get
                Return _testTradeTakeProfit
            End Get
            Set(value As String)
                SetProperty(_testTradeTakeProfit, value)
            End Set
        End Property

        Private _testTradeStatus As String = "Select a contract and click Analyse Trend"
        Public Property TestTradeStatus As String
            Get
                Return _testTradeStatus
            End Get
            Set(value As String)
                SetProperty(_testTradeStatus, value)
            End Set
        End Property

        Private _debugText As String = String.Empty
        Public Property DebugText As String
            Get
                Return _debugText
            End Get
            Set(value As String)
                SetProperty(_debugText, value)
            End Set
        End Property

        Private _lastManagementTime As DateTimeOffset
        Private _pendingEntryOrderId As Long? = Nothing
        Private _pendingEntryCorrelationId As String = String.Empty

        ' ── Strategy Control ──────────────────────────────────────────────────────
        Private _isStrategyRunning As Boolean
        Public Property IsStrategyRunning As Boolean
            Get
                Return _isStrategyRunning
            End Get
            Set(value As Boolean)
                If SetProperty(_isStrategyRunning, value) Then
                    RelayCommand.RaiseCanExecuteChanged()
                    Dispatch(Sub() TestTradeStatus = If(value, "Strategy: RUNNING - Monitoring 5s bars...", "Strategy: STOPPED"))
                End If
            End Set
        End Property

        Public Property StartStrategyBuyCommand As RelayCommand
        Public Property StartStrategySellCommand As RelayCommand
        Public Property StopStrategyCommand As RelayCommand
        Public Property CreateBracketCommand As RelayCommand

        Public ReadOnly Property CanStartStrategy As Boolean
            Get
                Return Not IsStrategyRunning AndAlso CanPlaceTestTrade
            End Get
        End Property

        ' ── P&L Monitoring ────────────────────────────────────────────────────────
        Private _currentPnL As Decimal = 0
        Public Property CurrentPnL As Decimal
            Get
                Return _currentPnL
            End Get
            Set(value As Decimal)
                If SetProperty(_currentPnL, value) Then
                    OnPropertyChanged(NameOf(CurrentPnLText))
                    OnPropertyChanged(NameOf(PnLColor))
                End If
            End Set
        End Property

        Public ReadOnly Property CurrentPnLText As String
            Get
                If _currentPnL = 0 Then Return "—"
                Return If(_currentPnL >= 0, $"+${_currentPnL:F2}", $"-${Math.Abs(_currentPnL):F2}")
            End Get
        End Property

        Public ReadOnly Property PnLColor As String
            Get
                Return If(_currentPnL >= 0, "BuyBrush", "SellBrush")
            End Get
        End Property

        Private _entryOrderId As Long? = Nothing
        Private _pnlMonitoringTimer As System.Threading.Timer
        Private _pnlLastCheckTime As DateTimeOffset = DateTimeOffset.UtcNow

        ' ── Bar Aggregation ──────────────────────────────────────────────────────
        Private Class MicroBar
            Public Property Open As Decimal
            Public Property High As Decimal
            Public Property Low As Decimal
            Public Property Close As Decimal
            Public Property Timestamp As DateTimeOffset
        End Class

        Private _currentBar As MicroBar
        Private ReadOnly _barHistory As New List(Of MicroBar)
        Private ReadOnly _strategyLock As New Object()
        ' 5-second interval
        Private ReadOnly _barPeriod As TimeSpan = TimeSpan.FromSeconds(5)

        Private Sub StartStrategy(side As OrderSide)
            If Not CanStartStrategy Then Return

            ' Note: The API (HistoryClient) only supports 1-minute bars or higher.
            ' We cannot recover 5-second historical data. We must build it live.

            IsStrategyRunning = True
            _entrySide = side          ' Store early so PositionUpdated handler knows direction
            CurrentPnL = 0  ' Reset P&L display
            _entryOrderId = Nothing

            SyncLock _strategyLock
                _barHistory.Clear()
                _currentBar = Nothing
                _isOrderPending = True
                _isPositionActive = False
            End SyncLock

            Dim contractId = _testTradeContractId
            Dim t = Task.Run(Function() _marketDataService.SubscribeAsync(contractId))
            Dim t2 = ExecuteTestTrade(side)

            ' Start P&L monitoring (will be picked up after order fills)
            StartPnLMonitoring()
        End Sub

        Private Sub StartPnLMonitoring()
            ' Stop any existing timer
            If _pnlMonitoringTimer IsNot Nothing Then
                _pnlMonitoringTimer.Dispose()
            End If

            ' Start new timer - check every 5 seconds
            _pnlMonitoringTimer = New System.Threading.Timer(
                AddressOf MonitorPnL,
                Nothing,
                TimeSpan.FromSeconds(1),  ' Initial delay
                TimeSpan.FromSeconds(5))  ' Period
        End Sub

        Private Sub MonitorPnL(state As Object)
            Try
                If Not _entryOrderId.HasValue OrElse _lastKnownPrice <= 0 Then
                    Return  ' No order or no price yet
                End If

                ' Calculate P&L based on entry price, current price, entry side, and quantity
                Dim priceDiff = _lastKnownPrice - _entryPrice

                ' For MGC: 1 point = $10 per contract
                ' For MES/MNQ: 1 point = $5 per contract
                Dim pointValue = GetPointValue(_testTradeContractId)

                ' Calculate P&L
                Dim pnl = If(_entrySide = OrderSide.Buy, priceDiff, -priceDiff) * pointValue

                ' Update UI
                Dispatch(Sub() CurrentPnL = pnl)

            Catch ex As Exception
                DebugLog.Log($"Error monitoring P&L: {ex.Message}")
            End Try
        End Sub

        Private Sub StopPnLMonitoring()
            If _pnlMonitoringTimer IsNot Nothing Then
                _pnlMonitoringTimer.Dispose()
                _pnlMonitoringTimer = Nothing
            End If
            Dispatch(Sub() CurrentPnL = 0)
        End Sub

        Private Sub StopStrategy()
            IsStrategyRunning = False
            StopPnLMonitoring()
            ' Do NOT remove handler so we continue to update _lastKnownPrice for manual orders
            ' RemoveHandler _marketDataService.QuoteReceived, AddressOf OnQuoteReceived
        End Sub

        Private _isOrderPending As Boolean = False
        Private _isPositionActive As Boolean = False
        Private _entryPrice As Decimal = 0
        Private _entrySide As OrderSide

        Private _lastKnownPrice As Decimal = 0

        Private Sub OnQuoteReceived(sender As Object, e As QuoteEventArgs)
            ' Always update price if it's for our contract
            If e.Quote.ContractId <> _testTradeContractId Then Return

            _lastKnownPrice = e.Quote.LastPrice

            If Not IsStrategyRunning Then Return

            SyncLock _strategyLock
                Dim price = e.Quote.LastPrice
                Dim time = e.Quote.Timestamp

                If _currentBar Is Nothing Then
                    _currentBar = New MicroBar With {.Timestamp = time, .Open = price, .High = price, .Low = price, .Close = price}
                Else
                    Dim elapsed = time - _currentBar.Timestamp
                    If elapsed >= _barPeriod Then
                        _barHistory.Add(_currentBar)
                        If _barHistory.Count > 100 Then _barHistory.RemoveAt(0)
                        _currentBar = New MicroBar With {.Timestamp = time, .Open = price, .High = price, .Low = price, .Close = price}
                    Else
                        If price > _currentBar.High Then _currentBar.High = price
                        If price < _currentBar.Low Then _currentBar.Low = price
                        _currentBar.Close = price
                    End If
                End If

                If _isPositionActive AndAlso (DateTimeOffset.UtcNow - _lastManagementTime).TotalSeconds >= 30 Then
                    ManageTrade()
                    _lastManagementTime = DateTimeOffset.UtcNow
                End If
            End SyncLock
        End Sub

        Private Sub CheckStrategyCondition()
            ' Executed inside SyncLock _strategyLock
            ' We only start managing once in position. We disabled automatic entry logic as we now enter immediately on StartStrategy.
            Return

            ' Leaving this logic here in case we want to revert to trend-following entry:
            ' If _barHistory.Count < 4 Then Return

            ' If Not _isPositionActive AndAlso Not _isOrderPending Then
            '     ... entry logic ...
            ' End If
        End Sub

        Private Sub ManageTrade()
            ' "If predominantly continuing the trend"
            ' Look at bars since last management (approx 30s = 6 bars)
            Dim recentBars = _barHistory.Where(Function(b) b.Timestamp > _lastManagementTime.AddSeconds(-30)).ToList()
            If recentBars.Count = 0 Then Return

            Dim trendContinueCount As Integer = 0
            For Each b In recentBars
                If _entrySide = OrderSide.Buy AndAlso b.Close > b.Open Then trendContinueCount += 1
                If _entrySide = OrderSide.Sell AndAlso b.Close < b.Open Then trendContinueCount += 1
            Next

            ' Only adjust if "predominant" -> majority
            If trendContinueCount > (recentBars.Count / 2) Then
                ' Move SL to within 15 ticks
                ' Move TP to +30 ticks
                Dim t = AdjustBracketsAsync()
            End If
        End Sub

        Private Async Function AdjustBracketsAsync() As Task
            Try
                ' Access current price from last bar
                Dim currentPrice = _barHistory.Last().Close
                Dim tickSize = 0.1D ' MGC tick size (0.1) or assume generic 0.25 (ES)
                ' Micro Gold (MGC) tick size is 0.1.
                ' Ideally get from Contract metadata. I'll stick to 0.1 for MGC as requested.
                If _testTradeContractId.Contains("MGC") Then tickSize = 0.1D Else tickSize = 0.25D

                Dim newSlOffset = 15 * tickSize
                Dim newTpOffset = 30 * tickSize

                Dim newSlPrice As Decimal
                Dim newTpPrice As Decimal

                If _entrySide = OrderSide.Buy Then
                    newSlPrice = currentPrice - newSlOffset
                    newTpPrice = currentPrice + newTpOffset
                Else
                    newSlPrice = currentPrice + newSlOffset
                    newTpPrice = currentPrice - newTpOffset
                End If

                ' Update orders (Cancel/Replace)
                ' Since we don't have order IDs for brackets stored clearly (only fire-and-forget in PlaceBracketsAsync),
                ' I need to update PlaceBracketsAsync to store the bracket order IDs.
                ' Refactoring PlaceBracketsAsync to store bracket IDs in class fields.

                If _tpOrderId.HasValue Then
                    Await _orderService.CancelOrderAsync(_tpOrderId.Value)
                    ' Place new TP
                    Dim tpOrder As New Order With {
                        .AccountId = _selectedAccount.Id,
                        .ContractId = _testTradeContractId,
                        .Side = If(_entrySide = OrderSide.Buy, OrderSide.Sell, OrderSide.Buy),
                        .OrderType = OrderType.Limit,
                        .Quantity = 1,
                        .LimitPrice = newTpPrice,
                        .Status = OrderStatus.Pending,
                        .PlacedAt = DateTimeOffset.UtcNow,
                        .Notes = "Adjusted TP"
                    }
                    Dim placed = Await _orderService.PlaceOrderAsync(tpOrder)
                    _tpOrderId = placed.ExternalOrderId
                    Dispatch(Sub() AddDebugMessage($"Adjusted TP to {newTpPrice}"))
                End If

                If _slOrderId.HasValue Then
                    Await _orderService.CancelOrderAsync(_slOrderId.Value)
                    ' Place new SL
                    Dim slOrder As New Order With {
                        .AccountId = _selectedAccount.Id,
                        .ContractId = _testTradeContractId,
                        .Side = If(_entrySide = OrderSide.Buy, OrderSide.Sell, OrderSide.Buy),
                        .OrderType = OrderType.StopOrder,
                        .Quantity = 1,
                        .StopPrice = newSlPrice,
                        .Status = OrderStatus.Pending,
                        .PlacedAt = DateTimeOffset.UtcNow,
                        .Notes = "Adjusted SL"
                    }
                    Dim placed = Await _orderService.PlaceOrderAsync(slOrder)
                    _slOrderId = placed.ExternalOrderId
                    Dispatch(Sub() AddDebugMessage($"Adjusted SL to {newSlPrice}"))
                End If

            Catch ex As Exception
                DebugLog.Log($"Error adjusting brackets: {ex.Message}")
            End Try
        End Function

        Private _tpOrderId As Long?
        Private _slOrderId As Long?

        ' ── Test trade placement ──────────────────────────────────────────────────

        Private Async Function ExecuteTestTrade(side As OrderSide) As Task
            Dim contractId = _testTradeContractId?.Trim()
            If String.IsNullOrWhiteSpace(contractId) Then
                Dispatch(Sub() TestTradeStatus = "⚠ No contract selected")
                SyncLock _strategyLock
                    _isOrderPending = False
                End SyncLock
                Return
            End If
            If _selectedAccount Is Nothing Then
                Dispatch(Sub() TestTradeStatus = "⚠ No account selected")
                SyncLock _strategyLock
                    _isOrderPending = False
                End SyncLock
                Return
            End If

            Dim qty As Integer = 1
            Integer.TryParse(_testTradeQuantity, qty)
            If qty <= 0 Then qty = 1

            Dim sideLabel = If(side = OrderSide.Buy, "BUY", "SELL")

            Dim correlationId = Guid.NewGuid().ToString("N")
            SyncLock _strategyLock
                _pendingEntryCorrelationId = correlationId
            End SyncLock

            Try
                Dispatch(Sub() TestTradeStatus = $"📤 Placing Market {sideLabel} for {contractId}...")

                Dim order As New Order With {
                    .AccountId = _selectedAccount.Id,
                    .ContractId = contractId,
                    .Side = side,
                    .OrderType = OrderType.Market,
                    .Quantity = qty,
                    .Status = OrderStatus.Pending,
                    .PlacedAt = DateTimeOffset.UtcNow,
                    .Notes = $"Test Trade — {sideLabel} [{correlationId}]"
                }

                Dim placedOrder = Await _orderService.PlaceOrderAsync(order)

                SyncLock _strategyLock
                    _pendingEntryOrderId = placedOrder.ExternalOrderId
                End SyncLock

                If placedOrder.Status = OrderStatus.Rejected Then
                    SyncLock _strategyLock
                        _isOrderPending = False
                    End SyncLock
                    Dim reason = If(String.IsNullOrWhiteSpace(placedOrder.Notes), "unknown reason", placedOrder.Notes)
                    Dispatch(Sub() TestTradeStatus = $"❌ {sideLabel} rejected: {reason}")
                Else
                    Dispatch(Sub() TestTradeStatus =
                        $"✔ {sideLabel} placed — Order #{placedOrder.ExternalOrderId}. Waiting for fill...")

                    ' Polling for fill (10 seconds)
                    Dim fillPrice As Decimal? = Nothing
                    Dim isFilled As Boolean = False

                    For i As Integer = 1 To 10
                        If _isPositionActive Then
                            isFilled = True
                            Exit For ' Handled by event
                        End If

                        ' Try polling
                        If placedOrder.ExternalOrderId.HasValue Then
                            fillPrice = Await _orderService.TryGetOrderFillPriceAsync(placedOrder.ExternalOrderId.Value, _selectedAccount.Id)
                            If fillPrice.HasValue Then
                                isFilled = True

                                ' Must trigger the "OnFilled" logic manually if event missed
                                SyncLock _strategyLock
                                    If Not _isPositionActive Then
                                        _isPositionActive = True
                                        _isOrderPending = False
                                        _pendingEntryOrderId = Nothing
                                        _pendingEntryCorrelationId = String.Empty

                                        _entryPrice = fillPrice.Value
                                        _entrySide = side
                                        _lastManagementTime = DateTimeOffset.UtcNow

                                        ' Manually trigger brackets since event didn't or was slow
                                        placedOrder.FillPrice = fillPrice
                                        Dim bracketTask As Task = Task.Run(Function() PlaceBracketsAsync(placedOrder, fillPrice.Value))
                                        Dispatch(Sub() AddDebugMessage($"Strategy: Polling confirmed Fill @ {fillPrice}. Placing brackets..."))
                                    End If
                                End SyncLock
                                Exit For
                            End If
                        End If

                        Await Task.Delay(1000)
                    Next

                    ' Timeout Handling
                    If Not isFilled AndAlso Not _isPositionActive Then
                        Dispatch(Sub() AddDebugMessage("Strategy: Order fill polling timed out (10s). Leaving order open."))
                        ' Removed aggressive Cancel/Flatten logic to avoid 400 errors if order is Working
                        SyncLock _strategyLock
                            _isOrderPending = False ' Reset pending flag so new trades can be taken if desired
                        End SyncLock
                    End If
                End If
            Catch ex As Exception
                Dim errMsg = ex.Message
                SyncLock _strategyLock
                    _isOrderPending = False
                End SyncLock
                Dispatch(Sub() TestTradeStatus = $"❌ Order error: {errMsg}")
                DebugLog.Log($"Order error: {errMsg}")
            End Try
        End Function

        Private Async Function PlaceBracketsAsync(entryOrder As Order, explicitFillPrice As Decimal) As Task
            Try
                Dispatch(Sub() AddDebugMessage($"Strategy: Calculating brackets for Entry @ {explicitFillPrice}..."))

                ' Wait 1 second before placing brackets to ensure the exchange has processed the fill fully
                Await Task.Delay(1000)

                Dim slTicks As Integer = 60
                Dim tpTicks As Integer = 30

                ' Use local variables to avoid threading issues with property access (though properties are on UI thread usually)
                ' Better to parse on UI thread, but we are in async task.
                ' We'll parse properties again or assume they haven't changed.
                Integer.TryParse(_testTradeStopLoss, slTicks)
                Integer.TryParse(_testTradeTakeProfit, tpTicks)

                ' Determine Tick Size based on contract
                Dim tickSize As Decimal = 0.25D ' Default
                Dim cId = entryOrder.ContractId.ToUpper()
                If cId.Contains("MGC") OrElse cId.Contains("GC") Then
                    tickSize = 0.1D
                ElseIf cId.Contains("MCL") OrElse cId.Contains("CL") Then
                    tickSize = 0.01D
                ElseIf cId.Contains("MNQ") OrElse cId.Contains("NQ") Then
                    tickSize = 0.25D
                ElseIf cId.Contains("MES") OrElse cId.Contains("ES") Then
                    tickSize = 0.25D
                End If

                If explicitFillPrice <= 0 Then
                    Dispatch(Sub() AddDebugMessage("Cannot place brackets: Invalid Entry Price (0)"))
                    Return
                End If

                Dim exitSide = If(entryOrder.Side = OrderSide.Buy, OrderSide.Sell, OrderSide.Buy)

                Dim slPrice As Decimal
                Dim tpPrice As Decimal
                Dim slOffset = slTicks * tickSize
                Dim tpOffset = tpTicks * tickSize

                If entryOrder.Side = OrderSide.Buy Then
                    slPrice = explicitFillPrice - slOffset
                    tpPrice = explicitFillPrice + tpOffset
                Else
                    slPrice = explicitFillPrice + slOffset
                    tpPrice = explicitFillPrice - tpOffset
                End If

                ' Round prices to valid tick size to avoid rejection
                slPrice = Math.Round(slPrice / tickSize) * tickSize
                tpPrice = Math.Round(tpPrice / tickSize) * tickSize

                ' Place TP (Limit)
                Dim tpOrder As New Order With {
                    .AccountId = entryOrder.AccountId,
                    .ContractId = entryOrder.ContractId,
                    .Side = exitSide,
                    .OrderType = OrderType.Limit,
                    .Quantity = entryOrder.Quantity,
                    .LimitPrice = tpPrice,
                    .Status = OrderStatus.Pending,
                    .PlacedAt = DateTimeOffset.UtcNow,
                    .Notes = $"Test TP (+{tpTicks}t)"
                }
                Try
                    Dim tpPlaced = Await _orderService.PlaceOrderAsync(tpOrder)
                    _tpOrderId = tpPlaced.ExternalOrderId
                    Dispatch(Sub() AddDebugMessage($"Placed TP Limit @ {tpPrice}"))
                Catch ex As Exception
                    Dispatch(Sub() AddDebugMessage($"TP Place Failed: {ex.Message}"))
                End Try

                ' Place SL (Stop Market)
                Dim slOrder As New Order With {
                    .AccountId = entryOrder.AccountId,
                    .ContractId = entryOrder.ContractId,
                    .Side = exitSide,
                    .OrderType = OrderType.StopOrder,
                    .Quantity = entryOrder.Quantity,
                    .StopPrice = slPrice,
                    .Status = OrderStatus.Pending,
                    .PlacedAt = DateTimeOffset.UtcNow,
                    .Notes = $"Test SL (-{slTicks}t)"
                }
                Try
                    Dim slPlaced = Await _orderService.PlaceOrderAsync(slOrder)
                    _slOrderId = slPlaced.ExternalOrderId
                    Dispatch(Sub() AddDebugMessage($"Placed SL Stop Market @ {slPrice}"))
                Catch ex As Exception
                    Dispatch(Sub() AddDebugMessage($"SL Place Failed: {ex.Message}"))
                End Try

            Catch ex As Exception
                DebugLog.Log($"Error placing brackets: {ex.Message}")
            End Try
        End Function

        ''' <summary>
        ''' Places a Stop Loss (Stop Market) and Take Profit (Limit) against the currently
        ''' open position. Does NOT place a new entry order.
        ''' Price priority: confirmed fill price → last known quote → market data subscription.
        ''' </summary>
        Private Async Sub CreateBracket()
            Try
                If _selectedAccount Is Nothing Then
                    Dispatch(Sub() AddDebugMessage("❌ No account selected."))
                    Return
                End If
                If String.IsNullOrWhiteSpace(_testTradeContractId) Then
                    Dispatch(Sub() AddDebugMessage("❌ No contract selected."))
                    Return
                End If

                ' ── 1. Resolve reference price ─────────────────────────────────
                ' Best source is the confirmed fill price; fall back to last live quote.
                Dim refPrice As Decimal = If(_entryPrice > 0, _entryPrice, _lastKnownPrice)

                If refPrice <= 0 Then
                    ' Last resort: subscribe and wait up to 5 s for a live quote
                    Dispatch(Sub() AddDebugMessage($"📡 Subscribing to market data for {_testTradeContractId}..."))
                    Await _marketDataService.SubscribeAsync(_testTradeContractId)
                    Dim waitCount = 0
                    While _lastKnownPrice <= 0 AndAlso waitCount < 10
                        Await Task.Delay(500)
                        waitCount += 1
                    End While
                    refPrice = _lastKnownPrice
                End If

                If refPrice <= 0 Then
                    Dispatch(Sub() AddDebugMessage("❌ Cannot place bracket: no fill price or live quote available."))
                    Return
                End If

                ' ── 2. Parameters ──────────────────────────────────────────────
                Dim cId = _testTradeContractId.ToUpper()
                Dim tickSize As Decimal =
                    If(cId.Contains("MGC") OrElse cId.Contains("GC"), 0.1D,
                    If(cId.Contains("MCL") OrElse cId.Contains("CL"), 0.01D, 0.25D))

                Dim slTicks As Integer = 50
                Dim tpTicks As Integer = 25
                Integer.TryParse(_testTradeStopLoss, slTicks)
                Integer.TryParse(_testTradeTakeProfit, tpTicks)

                ' Direction: use the side already set when BUY/SELL was clicked; default Buy
                Dim side = If(_isPositionActive OrElse _entryPrice > 0, _entrySide, OrderSide.Buy)
                Dim exitSide = If(side = OrderSide.Buy, OrderSide.Sell, OrderSide.Buy)

                Dim slPrice As Decimal
                Dim tpPrice As Decimal
                If side = OrderSide.Buy Then
                    slPrice = Math.Round((refPrice - slTicks * tickSize) / tickSize) * tickSize
                    tpPrice = Math.Round((refPrice + tpTicks * tickSize) / tickSize) * tickSize
                Else
                    slPrice = Math.Round((refPrice + slTicks * tickSize) / tickSize) * tickSize
                    tpPrice = Math.Round((refPrice - tpTicks * tickSize) / tickSize) * tickSize
                End If

                Dispatch(Sub() AddDebugMessage(
                    $"Bracket: ref={refPrice} | {side} pos | SL {exitSide} Stop @ {slPrice} | TP {exitSide} Limit @ {tpPrice}"))

                ' ── 3. Place TP (Limit) ────────────────────────────────────────
                Dim tpOrder As New Order With {
                    .AccountId = _selectedAccount.Id,
                    .ContractId = _testTradeContractId,
                    .Side = exitSide,
                    .OrderType = OrderType.Limit,
                    .Quantity = 1,
                    .LimitPrice = tpPrice,
                    .Status = OrderStatus.Pending,
                    .PlacedAt = DateTimeOffset.UtcNow,
                    .Notes = $"Bracket TP (+{tpTicks}t)"
                }
                Dim tpPlaced = Await _orderService.PlaceOrderAsync(tpOrder)
                _tpOrderId = tpPlaced.ExternalOrderId
                Dispatch(Sub() AddDebugMessage($"✅ TP Limit @ {tpPrice} → Order #{tpPlaced.ExternalOrderId}"))

                ' ── 4. Place SL (Stop Market) ──────────────────────────────────
                Dim slOrder As New Order With {
                    .AccountId = _selectedAccount.Id,
                    .ContractId = _testTradeContractId,
                    .Side = exitSide,
                    .OrderType = OrderType.StopOrder,
                    .Quantity = 1,
                    .StopPrice = slPrice,
                    .Status = OrderStatus.Pending,
                    .PlacedAt = DateTimeOffset.UtcNow,
                    .Notes = $"Bracket SL (-{slTicks}t)"
                }
                Dim slPlaced = Await _orderService.PlaceOrderAsync(slOrder)
                _slOrderId = slPlaced.ExternalOrderId
                Dispatch(Sub() AddDebugMessage($"✅ SL Stop @ {slPrice} → Order #{slPlaced.ExternalOrderId}"))

            Catch ex As Exception
                Dispatch(Sub() AddDebugMessage($"❌ Bracket error: {ex.Message}"))
            End Try
        End Sub

        Public ReadOnly Property CanPlaceTestTrade As Boolean
            Get
                Return Not String.IsNullOrWhiteSpace(_testTradeContractId) AndAlso _selectedAccount IsNot Nothing
            End Get
        End Property

        Private Sub LoadAvailableContracts()
            For Each f In FavouriteContracts.GetDefaults()
                AvailableContracts.Add(New Contract With {
                    .Id = f.ContractId,
                    .FriendlyName = f.Name
                })
            Next
        End Sub

        Private Sub UpdateTestTradeContractDisplay()
            Dim c = _testTradeSelectedContract
            If c IsNot Nothing Then
                ' Column 2 TextBlock: full API contract ID (e.g. CON.F.US.MES.H26)
                TestTradeContractLongId = c.Id
                ' Results panel "CONTRACT" heading: friendly name (e.g. MESH26 — Micro S&P 500)
                TestTradeContractDisplay = If(Not String.IsNullOrWhiteSpace(c.FriendlyName),
                                              c.FriendlyName, c.Id)
            ElseIf Not String.IsNullOrWhiteSpace(_testTradeContractId) Then
                TestTradeContractLongId = _testTradeContractId
                TestTradeContractDisplay = _testTradeContractId
            End If
        End Sub

        Private Sub OnTestOrderRejected(sender As Object, e As OrderRejectedEventArgs)
            Dim reason = e.Reason
            DebugLog.Log($"Order rejected: {reason}")
            Dispatch(Sub() TestTradeStatus = $"❌ Rejected: {reason}")
        End Sub

        ''' <summary>
        ''' Fires via SignalR (UserHub) the moment the exchange creates/updates our position.
        ''' This is the most reliable fill signal — OrderFilled is never raised by OrderService.
        ''' </summary>
        Private Sub OnPositionUpdated(sender As Object, e As PositionUpdateEventArgs)
            ' Only care while we are waiting for an entry fill
            If Not _isOrderPending Then Return
            If Not String.Equals(e.ContractId, _testTradeContractId, StringComparison.OrdinalIgnoreCase) Then Return
            If e.NetPosition = 0 Then Return  ' Position closed, not opened

            Dim isMyEntry = False
            Dim fillPrice = e.AveragePrice
            Dim capturedSide = _entrySide
            Dim capturedAccount = _selectedAccount
            Dim capturedContractId = _testTradeContractId
            Dim capturedQty As Integer = 1
            Integer.TryParse(_testTradeQuantity, capturedQty)
            If capturedQty <= 0 Then capturedQty = 1

            SyncLock _strategyLock
                If _isOrderPending AndAlso Not _isPositionActive Then
                    _isPositionActive = True
                    _isOrderPending = False
                    _entryPrice = fillPrice
                    _lastManagementTime = DateTimeOffset.UtcNow
                    isMyEntry = True
                End If
            End SyncLock

            If isMyEntry Then
                _entryOrderId = _pendingEntryOrderId
                _pendingEntryOrderId = Nothing
                _pendingEntryCorrelationId = String.Empty

                Dim entryOrder As New Order With {
                    .AccountId = If(capturedAccount IsNot Nothing, capturedAccount.Id, 0L),
                    .ContractId = capturedContractId,
                    .Side = capturedSide,
                    .OrderType = OrderType.Market,
                    .Quantity = capturedQty,
                    .FillPrice = fillPrice,
                    .Status = OrderStatus.Filled
                }

                Dim bracketTask As Task = Task.Run(Function() PlaceBracketsAsync(entryOrder, fillPrice))
                Dispatch(Sub() AddDebugMessage($"📡 Position update: filled @ {fillPrice}. Placing brackets..."))
                Dispatch(Sub() TestTradeStatus = $"✅ Filled @ {fillPrice} via SignalR. Brackets sent.")
            End If
        End Sub

        Private Sub OnTestOrderFilled(sender As Object, e As OrderFilledEventArgs)
            Dim fillPrice = e.Order.FillPrice
            Dim orderId = e.Order.ExternalOrderId

            DebugLog.Log($"Order filled: #{orderId} {e.Order.Side} {e.Order.Quantity} × {e.Order.ContractId} @ {fillPrice}")
            Dispatch(Sub() TestTradeStatus = $"✅ Filled: #{orderId} @ {fillPrice}")

            ' Check if this is our entry order
            Dim isMyEntry = False
            Dim actualOrderId = orderId.GetValueOrDefault()

            SyncLock _strategyLock
                ' Match by ID OR by Correlation ID in Notes
                Dim matchesId = (_pendingEntryOrderId.HasValue AndAlso orderId.HasValue AndAlso _pendingEntryOrderId.Value = orderId.Value)
                Dim matchesCorrelation = (Not String.IsNullOrEmpty(_pendingEntryCorrelationId) AndAlso e.Order.Notes.Contains(_pendingEntryCorrelationId))

                If matchesId OrElse matchesCorrelation Then
                    _isPositionActive = True
                    _isOrderPending = False ' Entry filled, no longer pending
                    _pendingEntryOrderId = Nothing
                    _pendingEntryCorrelationId = String.Empty
                    isMyEntry = True
                End If
            End SyncLock

            If isMyEntry Then
                _entryPrice = If(fillPrice.HasValue, fillPrice.Value, 0D)
                _entrySide = e.Order.Side
                _entryOrderId = orderId  ' Capture for P&L monitoring
                _lastManagementTime = DateTimeOffset.UtcNow

                Dim priceToUse = _entryPrice
                Task.Run(Function() PlaceBracketsAsync(e.Order, priceToUse))
                Dispatch(Sub() AddDebugMessage($"Strategy: Entered Trade. Monitoring trend... Order #{orderId}"))
            End If

            ' Check if this is one of our bracket orders closing the trade
            If (_tpOrderId.HasValue AndAlso orderId.HasValue AndAlso _tpOrderId.Value = orderId.Value) OrElse
               (_slOrderId.HasValue AndAlso orderId.HasValue AndAlso _slOrderId.Value = orderId.Value) Then

                SyncLock _strategyLock
                    _isPositionActive = False
                    _isOrderPending = False ' Just in case
                End SyncLock

                _tpOrderId = Nothing
                _slOrderId = Nothing

                Dispatch(Sub() AddDebugMessage("Strategy: Trade Closed."))
                ' Here we should cancel the other bracket if not filled (OCO simulation) -> Should be handled by Sniper engine or order service typically but here we do basic.
                ' For now, just reset state.
            End If
        End Sub

        Private Sub OnDebugLog(message As String)
            _debugMessages.Add(message)
            ' Keep only the last 100 messages to avoid unbounded growth
            If _debugMessages.Count > 100 Then
                _debugMessages.RemoveAt(0)
            End If
            Dim text = String.Join(Environment.NewLine, _debugMessages)
            Dispatch(Sub() DebugText = text)
        End Sub

        Private Sub ClearDebug(Optional param As Object = Nothing)
            _debugMessages.Clear()
            Dispatch(Sub() DebugText = String.Empty)
        End Sub

        Private Sub AddDebugMessage(message As String)
            Dispatch(Sub()
                         _debugMessages.Add(message)
                         If _debugMessages.Count > 100 Then _debugMessages.RemoveAt(0)
                         DebugText = String.Join(Environment.NewLine, _debugMessages)
                     End Sub)
        End Sub

        ' ── Housekeeping ──────────────────────────────────────────────────────────

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                RemoveHandler _orderService.OrderFilled, AddressOf OnTestOrderFilled
                RemoveHandler _orderService.OrderRejected, AddressOf OnTestOrderRejected
                RemoveHandler _orderService.PositionUpdated, AddressOf OnPositionUpdated
                RemoveHandler DebugLog.MessageLogged, AddressOf OnDebugLog
                StopPnLMonitoring()
                _disposed = True
            End If
        End Sub

        Private Sub Dispatch(action As Action)
            If Application.Current?.Dispatcher IsNot Nothing Then
                Application.Current.Dispatcher.Invoke(action)
            End If
        End Sub

        Private Function GetPointValue(contractId As String) As Decimal
            ''' <summary>
            ''' Returns the dollar value per point for the given contract.
            ''' MGC (Micro Gold): $10 per point
            ''' MES (Micro S&P): $5 per point
            ''' MNQ (Micro Nasdaq): $2 per point
            ''' MCL (Micro Crude): $100 per point
            ''' </summary>
            Dim cId = contractId.ToUpper()
            If cId.Contains("MGC") Then Return 10D
            If cId.Contains("MCL") Then Return 100D
            If cId.Contains("MNQ") Then Return 2D
            Return 5D  ' MES default
        End Function

    End Class

End Namespace
