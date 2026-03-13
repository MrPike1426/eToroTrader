Imports System.Windows.Media
Imports TopStepTrader.Core.Events
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
                    NotifyPropertyChanged(NameOf(DownPct))
                End If
            End Set
        End Property

        ''' <summary>Bear score = 100 - UpPct.</summary>
        Public ReadOnly Property DownPct As Integer
            Get
                Return 100 - _upPct
            End Get
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

        ' ── Hydra indicator grid display properties ──────────────────────────────

        Private _gridClose As String = "—"
        Public Property GridClose As String
            Get
                Return _gridClose
            End Get
            Private Set(value As String)
                SetProperty(_gridClose, value)
            End Set
        End Property

        Private _gridCloud1 As String = "—"
        Public Property GridCloud1 As String
            Get
                Return _gridCloud1
            End Get
            Private Set(value As String)
                SetProperty(_gridCloud1, value)
            End Set
        End Property

        Private _gridCloud2 As String = "—"
        Public Property GridCloud2 As String
            Get
                Return _gridCloud2
            End Get
            Private Set(value As String)
                SetProperty(_gridCloud2, value)
            End Set
        End Property

        Private _gridTenkan As String = "—"
        Public Property GridTenkan As String
            Get
                Return _gridTenkan
            End Get
            Private Set(value As String)
                SetProperty(_gridTenkan, value)
            End Set
        End Property

        Private _gridKijun As String = "—"
        Public Property GridKijun As String
            Get
                Return _gridKijun
            End Get
            Private Set(value As String)
                SetProperty(_gridKijun, value)
            End Set
        End Property

        Private _gridEma21 As String = "—"
        Public Property GridEma21 As String
            Get
                Return _gridEma21
            End Get
            Private Set(value As String)
                SetProperty(_gridEma21, value)
            End Set
        End Property

        Private _gridEma50 As String = "—"
        Public Property GridEma50 As String
            Get
                Return _gridEma50
            End Get
            Private Set(value As String)
                SetProperty(_gridEma50, value)
            End Set
        End Property

        Private _gridAdx As String = "—"
        Public Property GridAdx As String
            Get
                Return _gridAdx
            End Get
            Private Set(value As String)
                SetProperty(_gridAdx, value)
            End Set
        End Property

        Private _gridDiPlus As String = "—"
        Public Property GridDiPlus As String
            Get
                Return _gridDiPlus
            End Get
            Private Set(value As String)
                SetProperty(_gridDiPlus, value)
            End Set
        End Property

        Private _gridDiMinus As String = "—"
        Public Property GridDiMinus As String
            Get
                Return _gridDiMinus
            End Get
            Private Set(value As String)
                SetProperty(_gridDiMinus, value)
            End Set
        End Property

        Private _gridMacd As String = "—"
        Public Property GridMacd As String
            Get
                Return _gridMacd
            End Get
            Private Set(value As String)
                SetProperty(_gridMacd, value)
            End Set
        End Property

        Private _gridMacdPrev As String = "—"
        Public Property GridMacdPrev As String
            Get
                Return _gridMacdPrev
            End Get
            Private Set(value As String)
                SetProperty(_gridMacdPrev, value)
            End Set
        End Property

        Private _gridStochRsi As String = "—"
        Public Property GridStochRsi As String
            Get
                Return _gridStochRsi
            End Get
            Private Set(value As String)
                SetProperty(_gridStochRsi, value)
            End Set
        End Property

        Private _gridRsi14 As String = "—"
        Public Property GridRsi14 As String
            Get
                Return _gridRsi14
            End Get
            Private Set(value As String)
                SetProperty(_gridRsi14, value)
            End Set
        End Property

        Private _gridLongScore As String = "—"
        Public Property GridLongScore As String
            Get
                Return _gridLongScore
            End Get
            Private Set(value As String)
                SetProperty(_gridLongScore, value)
            End Set
        End Property

        Private _gridShortScore As String = "—"
        Public Property GridShortScore As String
            Get
                Return _gridShortScore
            End Get
            Private Set(value As String)
                SetProperty(_gridShortScore, value)
            End Set
        End Property

        Private _gridConf As String = "—"
        Public Property GridConf As String
            Get
                Return _gridConf
            End Get
            Private Set(value As String)
                SetProperty(_gridConf, value)
            End Set
        End Property

        Private _gridExplain As String = "—"
        Public Property GridExplain As String
            Get
                Return _gridExplain
            End Get
            Private Set(value As String)
                SetProperty(_gridExplain, value)
            End Set
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
        Public Sub ApplyConfidence(e As ConfidenceUpdatedEventArgs)
            Dim isUp = (e.UpPct >= e.DownPct)
            Dim dominant = If(isUp, e.UpPct, e.DownPct)
            Dim tradeLabel = If(isUp, "Long", "Short")
            Dim arrow = If(isUp, "↑", "↓")

            ' ── ADX-gated effective confidence ────────────────────────────────────
            ' For the EmaRsi strategy (TotalConditions = 6) the ADX trend-strength
            ' gate is a binary prerequisite that sits OUTSIDE the six-condition
            ' weighted score.  Displaying the raw score (e.g. 100%) when ADX is
            ' failing is misleading: no trade will fire regardless of how strongly
            ' the other indicators are aligned.
            '
            ' Rule: effectiveDominant = 0 whenever the ADX gate blocks the signal.
            '       Direction (arrow / tradeLabel) is preserved from the raw score
            '       so the tile still reads "↓ Short — 0%" rather than a neutral
            '       50/50 — making it clear the bearish setup is intact but waiting
            '       for trend-strength confirmation.
            '
            ' MultiConfluence (TotalConditions = 7) embeds ADX as a counted
            ' confluence condition so AdxGatePassed is always True there — this
            ' branch never fires for that strategy.
            Dim effectiveDominant As Integer =
                If(e.TotalConditions = 6 AndAlso Not e.AdxGatePassed, 0, dominant)

            SummaryLine = $"{arrow} {tradeLabel} — {effectiveDominant}%"
            LastUpdated = DateTime.Now.ToString("HH:mm:ss")
            UpPct = e.UpPct
            AdxGatePassed = e.AdxGatePassed
            AdxValue = e.AdxValue
            CurrentConfidencePct = effectiveDominant
            If e.LastClose > 0 Then RecordPrice(e.LastClose, DateTime.Now)
            RefreshMarketStatus()

            ' ── Indicator grid display ────────────────────────────────────────────
            If e.LastClose > 0D Then GridClose = $"{e.LastClose:F2}"
            If e.TotalConditions = 7 Then
                ' ── Multi-Confluence: populate all Ichimoku + MACD + StochRSI columns ──
                If e.Cloud1 > 0D Then GridCloud1 = $"{e.Cloud1:F2}"
                If e.Cloud2 > 0D Then GridCloud2 = $"{e.Cloud2:F2}"
                If e.Tenkan > 0D Then GridTenkan = $"{e.Tenkan:F2}"
                If e.Kijun > 0D Then GridKijun = $"{e.Kijun:F2}"
                If e.Ema21 > 0D Then GridEma21 = $"{e.Ema21:F2}"
                If e.Ema50 > 0D Then GridEma50 = $"{e.Ema50:F2}"
                GridAdx = $"{e.AdxValue:F1}"
                GridDiPlus = $"{e.PlusDI:F1}"
                GridDiMinus = $"{e.MinusDI:F1}"
                GridMacd = $"{e.MacdHist:F4}"
                GridMacdPrev = $"{e.MacdHistPrev:F4}"
                GridStochRsi = $"{e.StochRsiK:F1}"
                GridLongScore = $"{e.LongCount}/{e.TotalConditions}"
                GridShortScore = $"{e.ShortCount}/{e.TotalConditions}"
            ElseIf e.TotalConditions = 6 Then
                ' ── EMA/RSI Combined: populate EMA21/50, RSI14, ADX, DI columns ──
                If e.Ema21 > 0D Then GridEma21 = $"{e.Ema21:F2}"
                If e.Ema50 > 0D Then GridEma50 = $"{e.Ema50:F2}"
                GridRsi14 = If(e.Rsi14 > 0F, $"{e.Rsi14:F1}", "—")
                GridAdx = $"{e.AdxValue:F1}"
                If e.PlusDI > 0F Then GridDiPlus = $"{e.PlusDI:F1}"
                If e.MinusDI > 0F Then GridDiMinus = $"{e.MinusDI:F1}"
            ElseIf e.AdxValue > 0F Then
                GridAdx = $"{e.AdxValue:F1}"
            End If
            Dim maxPct = Math.Max(e.UpPct, e.DownPct)
            If maxPct > 0 Then
                ' GridConf matches the headline SummaryLine: show effectiveDominant so the
                ' indicator grid column is consistent with the tile confidence badge.
                GridConf = $"{effectiveDominant}% {If(e.UpPct > e.DownPct, "Long", "Short")}"
            End If
            GridExplain = BuildExplainText(e)
        End Sub

        ''' <summary>
        ''' Routes to the correct strategy explain builder based on <see cref="ConfidenceUpdatedEventArgs.TotalConditions"/>.
        ''' TotalConditions = 7 → Multi-Confluence (3-line Ichimoku/MACD/StochRSI summary).
        ''' TotalConditions = 6 → EMA/RSI Combined (3-line EMA/RSI/ADX summary).
        ''' Otherwise → "—".
        ''' </summary>
        Private Shared Function BuildExplainText(e As ConfidenceUpdatedEventArgs) As String
            If e.TotalConditions = 7 Then
                Return BuildMultiConfluenceExplain(e)
            ElseIf e.TotalConditions = 6 Then
                Return BuildEmaRsiExplain(e)
            End If
            Return "—"
        End Function

        ''' <summary>
        ''' Negative-only summary for the EMA/RSI Combined strategy.
        ''' Only conditions that are failing (✗) are shown; returns "—" when all pass.
        ''' </summary>
        Private Shared Function BuildEmaRsiExplain(e As ConfidenceUpdatedEventArgs) As String
            Dim isLong = e.UpPct >= e.DownPct
            Dim negatives As New List(Of String)()
            If isLong Then
                If Not (e.Ema21 > e.Ema50) Then negatives.Add($"EMA21({e.Ema21:F0}) below EMA50({e.Ema50:F0}) ✗")
                If Not (e.LastClose > e.Ema21) Then negatives.Add($"Price below EMA21({e.Ema21:F0}) ✗")
                If Not (e.LastClose > e.Ema50) Then negatives.Add($"Price below EMA50({e.Ema50:F0}) ✗")
                If Not (e.Rsi14 >= 50F AndAlso e.Rsi14 < 70F) Then negatives.Add($"RSI14={e.Rsi14:F1} outside 50-70 trend zone ✗")
                If Not e.Ema21Rising Then negatives.Add("EMA21 falling ✗")
                If Not e.RecentCandlesBullish Then negatives.Add("3-bar bias: majority red ✗")
                If Not e.AdxGatePassed Then negatives.Add($"Trend strength too weak (ADX={e.AdxValue:F1}) ✗")
            Else
                If Not (e.Ema21 < e.Ema50) Then negatives.Add($"EMA21({e.Ema21:F0}) above EMA50({e.Ema50:F0}) ✗")
                If Not (e.LastClose < e.Ema21) Then negatives.Add($"Price above EMA21({e.Ema21:F0}) ✗")
                If Not (e.LastClose < e.Ema50) Then negatives.Add($"Price above EMA50({e.Ema50:F0}) ✗")
                If Not (e.Rsi14 >= 30F AndAlso e.Rsi14 < 50F) Then negatives.Add($"RSI14={e.Rsi14:F1} outside 30-50 downtrend zone ✗")
                If e.Ema21Rising Then negatives.Add("EMA21 rising ✗")
                If e.RecentCandlesBullish Then negatives.Add("3-bar bias: majority green ✗")
                If Not e.AdxGatePassed Then negatives.Add($"Trend strength too weak (ADX={e.AdxValue:F1}) ✗")
            End If
            Return If(negatives.Count > 0, String.Join(vbLf, negatives), "—")
        End Function

        ''' <summary>
        ''' Negative-only summary for the Multi-Confluence strategy.
        ''' Only conditions that are failing (✗) are shown; returns "—" when all pass.
        ''' </summary>
        Private Shared Function BuildMultiConfluenceExplain(e As ConfidenceUpdatedEventArgs) As String
            Dim isLong = e.UpPct >= e.DownPct
            Dim negatives As New List(Of String)()
            If isLong Then
                Dim cloudOk = e.LastClose > e.Cloud1
                Dim tkKjOk = e.Tenkan > e.Kijun
                Dim adxOk = CSng(e.AdxValue) >= 19.9F AndAlso e.PlusDI > e.MinusDI
                Dim macdOk = CSng(e.MacdHist) > 0F AndAlso CSng(e.MacdHist) > CSng(e.MacdHistPrev)
                Dim stochOk = CSng(e.StochRsiK) < 0.8F
                If Not cloudOk Then negatives.Add("Price hasn't cleared the Ichimoku cloud ✗")
                If Not tkKjOk Then negatives.Add("Tenkan is below Kijun ✗")
                If Not adxOk Then negatives.Add("Trend strength (ADX) not yet strong enough / no bullish bias ✗")
                If Not macdOk Then negatives.Add("MACD needs to turn up ✗")
                If Not stochOk Then negatives.Add("StochRSI is overbought — wait ✗")
            Else
                Dim cloudOk = e.LastClose < e.Cloud2
                Dim tkKjOk = e.Tenkan < e.Kijun
                Dim adxOk = CSng(e.AdxValue) >= 19.9F AndAlso e.MinusDI > e.PlusDI
                Dim macdOk = CSng(e.MacdHist) < 0F AndAlso CSng(e.MacdHist) < CSng(e.MacdHistPrev)
                Dim stochOk = CSng(e.StochRsiK) > 0.2F
                If Not cloudOk Then negatives.Add("Price hasn't broken under the Ichimoku cloud ✗")
                If Not tkKjOk Then negatives.Add("Tenkan is above Kijun ✗")
                If Not adxOk Then negatives.Add("Trend strength (ADX) not yet strong enough / no bearish bias ✗")
                If Not macdOk Then negatives.Add("MACD needs to turn down ✗")
                If Not stochOk Then negatives.Add("StochRSI is oversold — wait ✗")
            End If
            Return If(negatives.Count > 0, String.Join(vbLf, negatives), "—")
        End Function

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
            Dim sideLabel = If(side = Core.Enums.OrderSide.Buy, "LONG", "SHORT")
            TradeStatusLine = $"{sideLabel}  ${amount:F0}×{leverage}  P&L: —"
        End Sub

        ''' <summary>Called each PositionSynced tick to update the live P&amp;L on the card.</summary>
        Public Sub UpdateTradePnl(unrealizedPnlUsd As Decimal)
            If _tradeStatusLine = "—  No position" Then Return
            Dim sign = If(unrealizedPnlUsd >= 0D, "+", "")
            Dim pnlIdx = _tradeStatusLine.LastIndexOf("P&L:", StringComparison.Ordinal)
            Dim baseText = If(pnlIdx >= 0, _tradeStatusLine.Substring(0, pnlIdx), _tradeStatusLine & " ")
            TradeStatusLine = $"{baseText.TrimEnd()}  P&L: {sign}${unrealizedPnlUsd:F2}"
        End Sub

        ''' <summary>Called when the engine closes the position (SL/TP, trail, reversal, or neutral exit).</summary>
        Public Sub CloseTrade()
            TradeStatusLine = "—  No position"
            ClearTurtleStatus()
        End Sub

        ' ── Turtle bracket status ─────────────────────────────────────────────────

        Private _turtleStatusLine As String = String.Empty
        Public Property TurtleStatusLine As String
            Get
                Return _turtleStatusLine
            End Get
            Private Set(value As String)
                If SetProperty(_turtleStatusLine, value) Then
                    NotifyPropertyChanged(NameOf(HasTurtleStatus))
                End If
            End Set
        End Property

        Public ReadOnly Property HasTurtleStatus As Boolean
            Get
                Return Not String.IsNullOrEmpty(_turtleStatusLine)
            End Get
        End Property

        ''' <summary>
        ''' Called whenever the Turtle bracket state changes.
        ''' The status message is only updated when <paramref name="isAdvance"/> is True,
        ''' meaning a price level was hit and the SL has been stepped up to lock profit.
        ''' Initial bracket placement and engine-restart reattachment do not display a
        ''' message — the bracket is simply in effect, not yet confirmed by price action.
        ''' </summary>
        Public Sub ApplyTurtleBracket(bracketNumber As Integer, slPrice As Decimal, tpPrice As Decimal,
                                      isAdvance As Boolean)
            If isAdvance Then
                TurtleStatusLine = $"🐢 Turtle Applied: {DateTime.Now:HH:mm:ss}"
                TopStepTrader.UI.Infrastructure.TurtleClickSound.PlayAsync()
            End If
        End Sub

        ''' <summary>Clears the Turtle bracket status message (called on position close).</summary>
        Public Sub ClearTurtleStatus()
            TurtleStatusLine = String.Empty
        End Sub

    End Class

End Namespace

