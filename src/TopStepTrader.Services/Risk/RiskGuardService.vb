Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Events
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.Data.Entities
Imports TopStepTrader.Data.Repositories

Namespace TopStepTrader.Services.Risk

    ''' <summary>
    ''' SAFETY-CRITICAL: Evaluates daily P&amp;L and drawdown against RiskSettings limits.
    ''' Once halted, trading remains halted for the session unless manually reset from the UI.
    ''' This service is the LAST gate before every order placement.
    ''' </summary>
    Public Class RiskGuardService
        Implements IRiskGuardService

        Private ReadOnly _settings As RiskSettings
        Private ReadOnly _orderRepo As OrderRepository
        Private ReadOnly _logger As ILogger(Of RiskGuardService)
        Private ReadOnly _haltLock As New Object()
        Private _isHalted As Boolean = False
        Private _haltReason As RiskHaltReason = RiskHaltReason.None

        Public Event TradingHalted As EventHandler(Of RiskHaltEventArgs) Implements IRiskGuardService.TradingHalted
        Public Event TradingResumed As EventHandler(Of EventArgs) Implements IRiskGuardService.TradingResumed

        Public ReadOnly Property IsHalted As Boolean Implements IRiskGuardService.IsHalted
            Get
                SyncLock _haltLock
                    Return _isHalted
                End SyncLock
            End Get
        End Property

        Public ReadOnly Property HaltReason As RiskHaltReason Implements IRiskGuardService.HaltReason
            Get
                SyncLock _haltLock
                    Return _haltReason
                End SyncLock
            End Get
        End Property

        Public Sub New(options As IOptions(Of RiskSettings),
                       orderRepo As OrderRepository,
                       logger As ILogger(Of RiskGuardService))
            _settings = options.Value
            _orderRepo = orderRepo
            _logger = logger
        End Sub

        ''' <summary>
        ''' Evaluates whether trading should continue. Returns True if safe, False if halted.
        ''' Automatically halts trading if limits are breached.
        ''' </summary>
        Public Async Function EvaluateRiskAsync(account As Account) As Task(Of Boolean) _
            Implements IRiskGuardService.EvaluateRiskAsync

            ' Already halted — keep halted
            SyncLock _haltLock
                If _isHalted Then
                    _logger.LogWarning("Trading is halted ({Reason}). Order blocked.", _haltReason)
                    Return False
                End If
            End SyncLock

            ' Check daily P&L
            Dim dailyPnL = Await GetDailyPnLAsync()
            If dailyPnL <= _settings.DailyLossLimitDollars Then
                Dim drawdown = Await GetCurrentDrawdownAsync()
                Halt(RiskHaltReason.DailyLossLimit, dailyPnL, drawdown)
                Return False
            End If

            ' Check drawdown from starting balance
            Dim currentDrawdown = account.Balance - account.StartingBalance
            If currentDrawdown <= _settings.MaxDrawdownDollars Then
                Dim pnl = Await GetDailyPnLAsync()
                Halt(RiskHaltReason.MaxDrawdown, pnl, currentDrawdown)
                Return False
            End If

            Return True
        End Function

        Public Async Function GetDailyPnLAsync() As Task(Of Decimal) _
            Implements IRiskGuardService.GetDailyPnLAsync
            Return Await _orderRepo.GetTodayPnLAsync()
        End Function

        Public Async Function GetCurrentDrawdownAsync() As Task(Of Decimal) _
            Implements IRiskGuardService.GetCurrentDrawdownAsync
            ' Drawdown approximated from today's P&L — real drawdown uses account balance
            Dim pnl = Await _orderRepo.GetTodayPnLAsync()
            Return If(pnl < 0D, pnl, 0D)
        End Function

        ''' <summary>
        ''' Manual reset — MUST only be called from an explicit UI confirmation action.
        ''' Logs the reason for audit trail.
        ''' </summary>
        Public Function ResetHaltAsync(reason As String) As Task _
            Implements IRiskGuardService.ResetHaltAsync
            SyncLock _haltLock
                _logger.LogWarning("Trading halt MANUALLY RESET. Reason: {Reason}", reason)
                _isHalted = False
                _haltReason = RiskHaltReason.None
            End SyncLock
            RaiseEvent TradingResumed(Me, EventArgs.Empty)
            Return Task.CompletedTask
        End Function

        Private Sub Halt(reason As RiskHaltReason, dailyPnL As Decimal, drawdown As Decimal)
            SyncLock _haltLock
                If _isHalted Then Return  ' Already halted
                _isHalted = True
                _haltReason = reason
            End SyncLock
            _logger.LogCritical(
                "TRADING HALTED! Reason: {Reason}. DailyPnL={PnL:C}, Drawdown={DD:C}",
                reason, dailyPnL, drawdown)
            RaiseEvent TradingHalted(Me, New RiskHaltEventArgs(reason, dailyPnL, drawdown))
        End Sub

    End Class

End Namespace
