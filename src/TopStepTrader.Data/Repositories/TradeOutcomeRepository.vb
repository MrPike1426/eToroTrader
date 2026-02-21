Imports Microsoft.EntityFrameworkCore
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Data.Entities

Namespace TopStepTrader.Data.Repositories

    ''' <summary>
    ''' Persists and queries real trade outcomes for the ML feedback loop.
    ''' </summary>
    Public Class TradeOutcomeRepository

        Private ReadOnly _db As AppDbContext

        Public Sub New(db As AppDbContext)
            _db = db
        End Sub

        ''' <summary>Save a new (initially open) trade outcome. Returns the assigned Id.</summary>
        Public Async Function SaveOutcomeAsync(outcome As TradeOutcome) As Task(Of Long)
            Dim entity As New TradeOutcomeEntity With {
                .SignalId        = outcome.SignalId,
                .OrderId         = outcome.OrderId,
                .ContractId      = outcome.ContractId,
                .Timeframe       = outcome.Timeframe,
                .SignalType      = outcome.SignalType,
                .SignalConfidence = outcome.SignalConfidence,
                .ModelVersion    = outcome.ModelVersion,
                .EntryTime       = outcome.EntryTime,
                .EntryPrice      = outcome.EntryPrice,
                .IsOpen          = True,
                .ExitReason      = String.Empty
            }
            _db.TradeOutcomes.Add(entity)
            Await _db.SaveChangesAsync()
            Return entity.Id
        End Function

        ''' <summary>Update an open outcome with the resolved exit data.</summary>
        Public Async Function ResolveOutcomeAsync(id         As Long,
                                                   exitTime   As DateTimeOffset,
                                                   exitPrice  As Decimal,
                                                   pnl        As Decimal,
                                                   isWinner   As Boolean,
                                                   exitReason As String) As Task
            Dim entity = Await _db.TradeOutcomes.FindAsync(id)
            If entity Is Nothing Then Return

            entity.ExitTime   = exitTime
            entity.ExitPrice  = exitPrice
            entity.PnL        = pnl
            entity.IsWinner   = isWinner
            entity.ExitReason = exitReason
            entity.IsOpen     = False

            Await _db.SaveChangesAsync()
        End Function

        ''' <summary>All outcomes still marked open (not yet resolved).</summary>
        Public Async Function GetOpenOutcomesAsync() As Task(Of List(Of TradeOutcomeEntity))
            Return Await _db.TradeOutcomes _
                            .AsNoTracking() _
                            .Where(Function(o) o.IsOpen) _
                            .OrderBy(Function(o) o.EntryTime) _
                            .ToListAsync()
        End Function

        ''' <summary>Resolved outcomes from the given lookback period, ordered by entry time.</summary>
        Public Async Function GetResolvedOutcomesAsync(from As DateTimeOffset) As Task(Of List(Of TradeOutcomeEntity))
            Return Await _db.TradeOutcomes _
                            .AsNoTracking() _
                            .Where(Function(o) Not o.IsOpen AndAlso o.EntryTime >= from) _
                            .OrderBy(Function(o) o.EntryTime) _
                            .ToListAsync()
        End Function

        ''' <summary>Rolling win-rate over the most recent N resolved outcomes.</summary>
        Public Async Function GetRollingWinRateAsync(windowSize As Integer) As Task(Of Single)
            Dim recent = Await _db.TradeOutcomes _
                                   .AsNoTracking() _
                                   .Where(Function(o) Not o.IsOpen AndAlso o.IsWinner.HasValue) _
                                   .OrderByDescending(Function(o) o.EntryTime) _
                                   .Take(windowSize) _
                                   .ToListAsync()
            If recent.Count = 0 Then Return 0
            Dim wins = recent.Where(Function(o) o.IsWinner = True).Count()
            Return CSng(wins) / CSng(recent.Count)
        End Function

        ''' <summary>Total number of outcomes (open + resolved).</summary>
        Public Async Function GetCountAsync() As Task(Of Integer)
            Return Await _db.TradeOutcomes.CountAsync()
        End Function

    End Class

End Namespace
