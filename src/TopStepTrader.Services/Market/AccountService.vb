Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.API.Http

Namespace TopStepTrader.Services.Market

    ''' <summary>
    ''' Implements IAccountService by calling the ProjectX Account/search endpoint.
    ''' </summary>
    Public Class AccountService
        Implements IAccountService

        Private ReadOnly _accountClient As AccountClient
        Private ReadOnly _logger As ILogger(Of AccountService)

        Public Sub New(accountClient As AccountClient, logger As ILogger(Of AccountService))
            _accountClient = accountClient
            _logger = logger
        End Sub

        Public Async Function GetActiveAccountsAsync() As Task(Of IEnumerable(Of Account)) _
            Implements IAccountService.GetActiveAccountsAsync
            Dim response = Await _accountClient.SearchAccountsAsync(onlyActive:=True)
            If response Is Nothing OrElse Not response.Success Then
                _logger.LogWarning("Account search failed: {Msg}", response?.ErrorMessage)
                Return Enumerable.Empty(Of Account)()
            End If
            Return response.Accounts.Select(Function(a) New Account With {
                .Id = a.Id,
                .Name = a.Name,
                .Balance = a.Balance,
                .CanTrade = a.CanTrade,
                .IsVisible = a.IsVisible,
                .StartingBalance = a.Balance  ' Approximate — real starting balance from combine rules
            })
        End Function

        Public Async Function GetAccountAsync(accountId As Long) As Task(Of Account) _
            Implements IAccountService.GetAccountAsync
            Dim all = Await GetActiveAccountsAsync()
            Return all.FirstOrDefault(Function(a) a.Id = accountId)
        End Function

    End Class

End Namespace
