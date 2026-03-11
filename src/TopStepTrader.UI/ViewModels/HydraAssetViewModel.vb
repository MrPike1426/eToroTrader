Imports System.Windows.Media
Imports TopStepTrader.UI.ViewModels.Base

Namespace TopStepTrader.UI.ViewModels

    ''' <summary>
    ''' Per-asset update-box ViewModel for the Hydra monitoring view.
    ''' Holds the latest EMA/RSI confidence snapshot for one instrument and
    ''' the derived market-open / closed state based on a simple schedule rule:
    '''   BTC              → always open (24/7)
    '''   OIL/GOLD/NSDQ100/SPX500 → closed on Saturday and Sunday (local time)
    ''' The rule is isolated here so it can later be replaced with an API-driven check.
    ''' </summary>
    Public Class HydraAssetViewModel
        Inherits ViewModelBase

        ' ── Static identity ───────────────────────────────────────────────────────
        Public ReadOnly Property Symbol As String
        Public ReadOnly Property Icon As String
        Public ReadOnly Property ContractId As String

        ' ── Live confidence state ─────────────────────────────────────────────────
        Private _lastUpdated As String = "—"
        Public Property LastUpdated As String
            Get
                Return _lastUpdated
            End Get
            Set(value As String)
                SetProperty(_lastUpdated, value)
            End Set
        End Property

        Private _summaryLine As String = "Awaiting first bar check…"
        Public Property SummaryLine As String
            Get
                Return _summaryLine
            End Get
            Set(value As String)
                SetProperty(_summaryLine, value)
            End Set
        End Property

        ' ── ADX display ──────────────────────────────────────────────────────────
        Private _adxValueF As Single = -1F   ' sentinel: -1 = no data received yet
        Public Property AdxValue As Single
            Get
                Return _adxValueF
            End Get
            Set(value As Single)
                Dim safeValue As Single = If(Single.IsNaN(value), -1F, value)
                If safeValue = _adxValueF Then Return
                _adxValueF = safeValue
                NotifyPropertyChanged(NameOf(AdxValue))
                NotifyPropertyChanged(NameOf(AdxDisplay))
                NotifyPropertyChanged(NameOf(AdxLineDisplay))
            End Set
        End Property

        ''' <summary>"ADX: X.X" when a value has been received; "ADX: —" until then.</summary>
        Public ReadOnly Property AdxDisplay As String
            Get
                Return If(_adxValueF >= 0, $"ADX: {_adxValueF:F1}", "ADX: —")
            End Get
        End Property

        ' ── 5-minute price-change buffer ─────────────────────────────────────────
        Private _priceTimes As New List(Of DateTime)()
        Private _pricePrices As New List(Of Decimal)()

        Private _change5mDisplay As String = "5m: —"
        Public Property Change30mDisplay As String   ' name kept for any existing callers
            Get
                Return _change5mDisplay
            End Get
            Private Set(value As String)
                If SetProperty(_change5mDisplay, value) Then
                    NotifyPropertyChanged(NameOf(AdxLineDisplay))
                End If
            End Set
        End Property

        ''' <summary>Combined one-liner: "ADX: 15.3  |  5m: +0.42%"</summary>
        Public ReadOnly Property AdxLineDisplay As String
            Get
                Dim adxPart = If(_adxValueF >= 0, $"ADX: {_adxValueF:F1}", "ADX: —")
                Return $"{adxPart}  |  {_change5mDisplay}"
            End Get
        End Property

        Private Sub RecordPrice(price As Decimal, timestamp As DateTime)
            _priceTimes.Add(timestamp)
            _pricePrices.Add(price)
            Dim cutoff = timestamp.AddMinutes(-8)
            Dim trim = 0
            While trim < _priceTimes.Count AndAlso _priceTimes(trim) < cutoff
                trim += 1
            End While
            If trim > 0 Then
                _priceTimes.RemoveRange(0, trim)
                _pricePrices.RemoveRange(0, trim)
            End If
            Dim target = timestamp.AddMinutes(-5)
            Dim bestIdx As Integer = -1
            Dim bestDiff = TimeSpan.MaxValue
            For j = 0 To _priceTimes.Count - 1
                Dim diff = (target - _priceTimes(j)).Duration()
                If diff < bestDiff AndAlso diff <= TimeSpan.FromMinutes(2) Then
                    bestDiff = diff
                    bestIdx = j
                End If
            Next
            If bestIdx >= 0 AndAlso _pricePrices(bestIdx) > 0 Then
                Dim pct = ((price - _pricePrices(bestIdx)) / _pricePrices(bestIdx)) * 100D
                Change30mDisplay = If(pct >= 0, $"5m: +{pct:F2}%", $"5m: {pct:F2}%")
            End If
        End Sub

        Private _upPct As Integer = 0
        Private _adxGatePassed As Boolean = True

        Public Property UpPct As Integer
            Get
                Return _upPct
            End Get
            Set(value As Integer)
                If SetProperty(_upPct, value) Then
                    NotifyPropertyChanged(NameOf(ConfidenceColor))
                    NotifyPropertyChanged(NameOf(DirectionForeground))
                End If
            End Set
        End Property

        Public Property AdxGatePassed As Boolean
            Get
                Return _adxGatePassed
            End Get
            Set(value As Boolean)
                If SetProperty(_adxGatePassed, value) Then
                    NotifyPropertyChanged(NameOf(ConfidenceColor))
                End If
            End Set
        End Property

        ''' <summary>
        ''' Dominant confidence percentage (max of UP / DOWN scores).
        ''' Used by StatusForeground to switch to ForestGreen when ≥ 80%.
        ''' </summary>
        Private _currentConfidencePct As Integer = 0
        Public Property CurrentConfidencePct As Integer
            Get
                Return _currentConfidencePct
            End Get
            Private Set(value As Integer)
                If SetProperty(_currentConfidencePct, value) Then
                    NotifyPropertyChanged(NameOf(StatusForeground))
                    NotifyPropertyChanged(NameOf(TileBackground))
                End If
            End Set
        End Property

        ' ── Market open / closed state ────────────────────────────────────────────
        Private _isMarketOpen As Boolean = False
        Public Property IsMarketOpen As Boolean
            Get
                Return _isMarketOpen
            End Get
            Private Set(value As Boolean)
                    If SetProperty(_isMarketOpen, value) Then
                        NotifyPropertyChanged(NameOf(AssetStatusText))
                        NotifyPropertyChanged(NameOf(StatusForeground))
                        NotifyPropertyChanged(NameOf(StatusBorderBrush))
                        NotifyPropertyChanged(NameOf(CardForeground))
                        NotifyPropertyChanged(NameOf(ConfidenceColor))
                        NotifyPropertyChanged(NameOf(TileBackground))
                    End If
                End Set
            End Property

        ''' <summary>"OPEN" or "CLOSED" — drives the status badge in the card.</summary>
        Public ReadOnly Property AssetStatusText As String
            Get
                Return If(_isMarketOpen, "OPEN", "CLOSED")
            End Get
        End Property

        ''' <summary>
        ''' Foreground colour for the OPEN / CLOSED status badge:
        '''   CLOSED  → Red
        '''   OPEN    → ForestGreen (always, consistent with whole-card requirement)
        ''' </summary>
        Public ReadOnly Property StatusForeground As SolidColorBrush
            Get
                If Not _isMarketOpen Then
                    Return New SolidColorBrush(Colors.Red)
                End If
                Return New SolidColorBrush(Color.FromRgb(&H22, &H8B, &H22))  ' ForestGreen
            End Get
        End Property

        ''' <summary>
        ''' Base foreground for the entire asset card.
        ''' ForestGreen when the market is open (tradeable); white otherwise.
        ''' Setting this on the container causes it to cascade to all child TextBlocks
        ''' that do not override Foreground explicitly.
        ''' </summary>
        Public ReadOnly Property CardForeground As SolidColorBrush
            Get
                If _isMarketOpen Then
                    Return New SolidColorBrush(Color.FromRgb(&H22, &H8B, &H22))  ' ForestGreen
                End If
                Return New SolidColorBrush(Colors.White)
            End Get
        End Property

        ''' <summary>
        ''' Full tile background: dark green when market is open and confidence ≥ 80%,
        ''' otherwise the standard card colour.
        ''' </summary>
        Public ReadOnly Property TileBackground As SolidColorBrush
            Get
                If _isMarketOpen AndAlso _currentConfidencePct >= 80 Then
                    Return New SolidColorBrush(Color.FromArgb(&HFF, &H0D, &H3A, &H18))
                End If
                Return New SolidColorBrush(Color.FromArgb(&HFF, &H24, &H31, &H56))
            End Get
        End Property

        ''' <summary>
        ''' Top-border accent colour for the asset card:
        '''   Market open  → Forest Green
        '''   Market closed → Red
        ''' </summary>
        Public ReadOnly Property StatusBorderBrush As SolidColorBrush
            Get
                If _isMarketOpen Then
                    Return New SolidColorBrush(Color.FromRgb(&H22, &H8B, &H22))
                End If
                Return New SolidColorBrush(Colors.Red)
            End Get
        End Property

        ''' <summary>
        ''' Foreground colour for the direction / confidence summary line:
        '''   Long bias (upPct > 50)  → Forest Green
        '''   Short bias (upPct &lt; 50) → Red
        '''   Neutral / no data      → muted grey
        ''' </summary>
        Public ReadOnly Property DirectionForeground As SolidColorBrush
            Get
                If _upPct > 50 Then
                    Return New SolidColorBrush(Color.FromRgb(&H22, &H8B, &H22))
                ElseIf _upPct < 50 AndAlso _upPct > 0 Then
                    Return New SolidColorBrush(Colors.Red)
                End If
                Return New SolidColorBrush(Color.FromArgb(&HFF, &H80, &H80, &HA0))
            End Get
        End Property

        ''' <summary>
        ''' Confidence bar colour (unchanged from original — separate from status badge):
        '''   ADX suppressed        → amber  (#FF9500)
        '''   dominant score ≥ 85%  → green  (#27AE60)
        '''   dominant score ≤ 35%  → red    (#E5533A)
        '''   otherwise             → muted  (#8080A0)
        ''' </summary>
        Public ReadOnly Property ConfidenceColor As SolidColorBrush
            Get
                If _isMarketOpen Then
                    Return New SolidColorBrush(Color.FromRgb(&H22, &H8B, &H22))  ' ForestGreen
                End If
                If _upPct = 0 Then
                    Return New SolidColorBrush(Color.FromArgb(&HFF, &H80, &H80, &HA0))
                End If
                If Not _adxGatePassed Then
                    Return New SolidColorBrush(Color.FromRgb(&HFF, &H95, &H00))
                End If
                Dim dominant = Math.Max(_upPct, 100 - _upPct)
                If dominant >= 85 Then
                    Return New SolidColorBrush(Color.FromRgb(&H27, &HAE, &H60))
                End If
                If dominant <= 35 Then
                    Return New SolidColorBrush(Color.FromRgb(&HE5, &H53, &H3A))
                End If
                Return New SolidColorBrush(Color.FromArgb(&HFF, &H80, &H80, &HA0))
            End Get
        End Property

        Public Sub New(symbol As String, icon As String, contractId As String)
            Me.Symbol = symbol
            Me.Icon = icon
            Me.ContractId = contractId
            RefreshMarketStatus()
        End Sub

        ''' <summary>
        ''' Crypto assets that trade 24/7 — always considered open regardless of day-of-week.
        ''' Add new tickers here as the universe expands.
        ''' </summary>
        Private Shared ReadOnly CryptoSymbols As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "BTC", "ETH", "XRP", "SOL", "BNB", "ADA", "DOGE", "AVAX", "MATIC", "DOT"
        }

        ''' <summary>
        ''' Recomputes IsMarketOpen from the current UTC time.
        ''' Rules:
        '''   Crypto symbols (BTC, ETH, …) → always open (24/7).
        '''   All other assets (indices, OIL, GOLD, …):
        '''     • Closed all day Saturday (UTC).
        '''     • Closed Sunday before 23:00 UTC (CME opens ~22:00–23:00 UTC Sun).
        '''     • Closed daily 21:00–22:00 UTC (CME ~5–6 PM ET maintenance break).
        ''' Isolated here so a future API-driven implementation replaces only this method.
        ''' </summary>
        Public Sub RefreshMarketStatus()
            Dim open As Boolean
            If CryptoSymbols.Contains(Symbol) Then
                open = True
            Else
                Dim now = DateTime.UtcNow
                Dim day = now.DayOfWeek
                Dim hour = now.Hour

                ' Closed all day Saturday
                If day = DayOfWeek.Saturday Then
                    open = False
                ' Closed Sunday before 23:00 UTC (CME reopens Sunday evening)
                ElseIf day = DayOfWeek.Sunday AndAlso hour < 23 Then
                    open = False
                ' Closed daily during CME maintenance 21:00–22:00 UTC (≈ 5–6 PM ET)
                ElseIf hour = 21 Then
                    open = False
                Else
                    open = True
                End If
            End If
            IsMarketOpen = open
        End Sub

        ''' <summary>
        ''' Called from the engine ConfidenceUpdated event on the UI dispatcher.
        ''' Updates all display properties atomically and refreshes market status.
        ''' </summary>
        Public Sub ApplyConfidence(upPct As Integer, downPct As Integer, adxGatePassed As Boolean,
                                     Optional adxValue As Single = 0,
                                     Optional lastClose As Decimal = 0D)
            Dim isUp = (upPct >= downPct)
            Dim dominant = If(isUp, upPct, downPct)
            Dim tradeLabel = If(isUp, "Long", "Short")
            Dim arrow = If(isUp, "↑", "↓")
            SummaryLine = $"{arrow} {tradeLabel} — {dominant}%"
            LastUpdated = DateTime.Now.ToString("HH:mm:ss")
            UpPct = upPct
            AdxGatePassed = adxGatePassed
            AdxValue = adxValue
            CurrentConfidencePct = dominant
            If lastClose > 0 Then RecordPrice(lastClose, DateTime.Now)
            RefreshMarketStatus()
        End Sub

        ' ── Live position / trail bracket display ─────────────────────────────────

        Private _tradeStatusLine As String = "—  No position"
        Public Property TradeStatusLine As String
            Get
                Return _tradeStatusLine
            End Get
            Private Set(value As String)
                SetProperty(_tradeStatusLine, value)
            End Set
        End Property

        ''' <summary>Called when the engine opens a new position on this asset.</summary>
        Public Sub OpenTrade(side As Core.Enums.OrderSide, entryPrice As Decimal, amount As Decimal, leverage As Integer)
            Dim sideLabel = If(side = Core.Enums.OrderSide.Buy, "🟢 LONG", "🔴 SHORT")
            TradeStatusLine = $"{sideLabel}  @{entryPrice:F0}  ${amount:F0}×{leverage}  | P&L: —"
        End Sub

        ''' <summary>Called each PositionSynced tick to update the live P&amp;L on the card.</summary>
        Public Sub UpdateTradePnl(unrealizedPnlUsd As Decimal)
            If _tradeStatusLine = "—  No position" Then Return
            Dim sign = If(unrealizedPnlUsd >= 0D, "+", "")
            Dim pnlIdx = _tradeStatusLine.LastIndexOf("| P&L:", StringComparison.Ordinal)
            Dim baseText = If(pnlIdx >= 0, _tradeStatusLine.Substring(0, pnlIdx), _tradeStatusLine & " ")
            TradeStatusLine = $"{baseText.TrimEnd()}  | P&L: {sign}${unrealizedPnlUsd:F2}"
        End Sub

        ''' <summary>Called when the engine closes the position (SL/TP, trail, reversal, or neutral exit).</summary>
        Public Sub CloseTrade()
            TradeStatusLine = "—  No position"
        End Sub

    End Class

End Namespace

