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

        Private _upPct As Integer = 0
        Private _adxGatePassed As Boolean = True

        Public Property UpPct As Integer
            Get
                Return _upPct
            End Get
            Set(value As Integer)
                If SetProperty(_upPct, value) Then
                    NotifyPropertyChanged(NameOf(ConfidenceColor))
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
        '''   CLOSED                       → Red
        '''   OPEN + confidence ≥ 80%      → ForestGreen
        '''   OPEN + confidence &lt; 80%   → muted (TextSecondaryBrush equivalent)
        ''' </summary>
        Public ReadOnly Property StatusForeground As SolidColorBrush
            Get
                If Not _isMarketOpen Then
                    Return New SolidColorBrush(Colors.Red)
                ElseIf _currentConfidencePct >= 80 Then
                    Return New SolidColorBrush(Color.FromRgb(&H22, &H8B, &H22))  ' ForestGreen
                Else
                    Return New SolidColorBrush(Color.FromArgb(&HFF, &H80, &H80, &HA0))
                End If
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
        ''' Confidence bar colour (unchanged from original — separate from status badge):
        '''   ADX suppressed        → amber  (#FF9500)
        '''   dominant score ≥ 85%  → green  (#27AE60)
        '''   dominant score ≤ 35%  → red    (#E5533A)
        '''   otherwise             → muted  (#8080A0)
        ''' </summary>
        Public ReadOnly Property ConfidenceColor As SolidColorBrush
            Get
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
        ''' Recomputes IsMarketOpen from the current local day-of-week.
        ''' Rule: known crypto symbols are always open (24/7);
        '''       all other assets (OIL, GOLD, indices) are closed Sat + Sun.
        ''' Isolated here so a future API-driven implementation replaces only this method.
        ''' </summary>
        Public Sub RefreshMarketStatus()
            Dim open As Boolean
            If CryptoSymbols.Contains(Symbol) Then
                open = True
            Else
                Dim day = DateTime.Now.DayOfWeek
                open = day <> DayOfWeek.Saturday AndAlso day <> DayOfWeek.Sunday
            End If
            IsMarketOpen = open
        End Sub

        ''' <summary>
        ''' Called from the engine ConfidenceUpdated event on the UI dispatcher.
        ''' Updates all display properties atomically and refreshes market status.
        ''' </summary>
        Public Sub ApplyConfidence(upPct As Integer, downPct As Integer, adxGatePassed As Boolean)
            Dim isUp = (upPct >= downPct)
            Dim dominant = If(isUp, upPct, downPct)
            Dim direction = If(isUp, "UP", "DOWN")
            Dim tradeLabel = If(isUp, "LONG", "SHORT")
            Dim arrow = If(isUp, "↑", "↓")
            Dim gateSuffix = If(Not adxGatePassed, " ⊘ ADX<25", "")
            SummaryLine = $"{arrow} {direction} {dominant}% ({tradeLabel}){gateSuffix}"
            LastUpdated = DateTime.Now.ToString("HH:mm:ss")
            UpPct = upPct
            AdxGatePassed = adxGatePassed
            CurrentConfidencePct = dominant
            RefreshMarketStatus()
        End Sub

    End Class

End Namespace

