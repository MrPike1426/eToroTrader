Imports System.Windows.Media
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.UI.ViewModels.Base

Namespace TopStepTrader.UI.ViewModels

    ''' <summary>
    ''' One row in the AI Trade performance table.
    ''' Starts as "In Progress" with a pulsing amber dot; closes to TP/SL result.
    ''' </summary>
    Public Class TradeRowViewModel
        Inherits ViewModelBase

        ' ── Immutable fields set at construction ──────────────────────────────────
        Public ReadOnly Property EntryTime As DateTimeOffset
        Public ReadOnly Property ContractDisplay As String
        Public ReadOnly Property ConfidencePct As Integer
        Public ReadOnly Property SideDisplay As String   ' "BUY" or "SELL"

        ' ── Formatted display of entry time (dd/MM HH:mm in local time) ──────────
        Public ReadOnly Property EntryTimeDisplay As String
            Get
                Return EntryTime.ToLocalTime().ToString("dd/MM HH:mm")
            End Get
        End Property

        ' ── Live / mutable: updated when position closes ───────────────────────────

        Private _result As String = "⏳ In Progress..."
        Public Property Result As String
            Get
                Return _result
            End Get
            Set(value As String)
                SetProperty(_result, value)
            End Set
        End Property

        Private _isInProgress As Boolean = True
        Public Property IsInProgress As Boolean
            Get
                Return _isInProgress
            End Get
            Set(value As Boolean)
                SetProperty(_isInProgress, value)
            End Set
        End Property

        Private _resultForeground As Brush = Brushes.White
        Public Property ResultForeground As Brush
            Get
                Return _resultForeground
            End Get
            Set(value As Brush)
                SetProperty(_resultForeground, value)
            End Set
        End Property

        ' ── Constructor ───────────────────────────────────────────────────────────

        Public Sub New(side As OrderSide, contractId As String,
                       confidencePct As Integer, entryTime As DateTimeOffset,
                       Optional externalOrderId As Long? = Nothing)
            Me.EntryTime = entryTime
            Me.ContractDisplay = ToFriendlyName(contractId)
            Me.ConfidencePct = confidencePct
            Me.SideDisplay = If(side = OrderSide.Buy, "BUY", "SELL")
            Me.ExternalOrderId = externalOrderId
        End Sub

        ' ── TopStepX order ID ────────────────────────────────────────────────────
        Public ReadOnly Property ExternalOrderId As Long?

        ''' <summary>Formatted TopStepX entry order ID for display ("—" when not available).</summary>
        Public ReadOnly Property TradeIdDisplay As String
            Get
                Return If(ExternalOrderId.HasValue, ExternalOrderId.Value.ToString(), "—")
            End Get
        End Property

        ' ── Close row with result ─────────────────────────────────────────────────

        ''' <summary>
        ''' Called when the position closes.
        ''' exitReason: "TP", "SL", or "Closed" (manual/unknown).
        ''' pnl: signed dollar amount (positive = profit, negative = loss).
        ''' </summary>
        Friend Sub Close(exitReason As String, pnl As Decimal)
            IsInProgress = False
            Dim sign = If(pnl >= 0, "+", "")
            Dim pnlStr = $"{sign}${CInt(Math.Round(pnl)):N0}"
            Select Case exitReason
                Case "TP"
                    Result = $"TP  {pnlStr}  ✅"
                    ResultForeground = New SolidColorBrush(Color.FromRgb(&H4A, &HBA, &H61))
                Case "SL"
                    Result = $"SL  {pnlStr}  ❌"
                    ResultForeground = New SolidColorBrush(Color.FromRgb(&HE5, &H53, &H3A))
                Case Else
                    Result = "Closed"
                    ResultForeground = New SolidColorBrush(Color.FromRgb(&H80, &H80, &H80))
            End Select
        End Sub

        ' ── Helpers ───────────────────────────────────────────────────────────────

        ''' <summary>
        ''' Maps a full contract ID to a short friendly display name.
        ''' TICKET-026 will replace this with an API-backed lookup.
        ''' </summary>
        Friend Shared Function ToFriendlyName(contractId As String) As String
            If contractId.Contains("MGC") Then Return "M.Gold"
            If contractId.Contains("MNQ") Then Return "M.Nasdaq"
            If contractId.Contains("MCL") Then Return "M.Oil"
            If contractId.Contains("MES") Then Return "M.S&P"
            Return contractId.Substring(0, Math.Min(6, contractId.Length))
        End Function

    End Class

End Namespace
