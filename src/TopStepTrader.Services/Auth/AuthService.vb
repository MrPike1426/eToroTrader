Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.API

Namespace TopStepTrader.Services.Auth

    ''' <summary>
    ''' Implements IAuthService by delegating to the API-layer TokenManager.
    ''' The TokenManager holds the actual JWT and manages refresh internally.
    ''' </summary>
    Public Class AuthService
        Implements IAuthService

        Private ReadOnly _tokenManager As TokenManager
        Private ReadOnly _logger As ILogger(Of AuthService)

        Public Sub New(tokenManager As TokenManager,
                       logger As ILogger(Of AuthService))
            _tokenManager = tokenManager
            _logger = logger
        End Sub

        Public ReadOnly Property CurrentToken As String Implements IAuthService.CurrentToken
            Get
                Return If(_tokenManager.CurrentToken, String.Empty)
            End Get
        End Property

        Public ReadOnly Property TokenExpiresAt As DateTimeOffset Implements IAuthService.TokenExpiresAt
            Get
                Return _tokenManager.TokenExpiresAt   ' Actual property name on TokenManager
            End Get
        End Property

        Public ReadOnly Property IsAuthenticated As Boolean Implements IAuthService.IsAuthenticated
            Get
                Return _tokenManager.IsAuthenticated   ' Actual property name on TokenManager
            End Get
        End Property

        Public Async Function LoginAsync(userName As String, apiKey As String) As Task(Of String) _
            Implements IAuthService.LoginAsync
            _logger.LogInformation("Logging in as {User}", userName)
            ' ForceRefreshAsync returns Task (not Task(Of String)); read CurrentToken after
            Await _tokenManager.ForceRefreshAsync()
            _logger.LogInformation("Login successful, token expires at {Exp}", _tokenManager.TokenExpiresAt)
            Return _tokenManager.CurrentToken
        End Function

        Public Async Function ValidateTokenAsync() As Task(Of Boolean) _
            Implements IAuthService.ValidateTokenAsync
            Try
                Dim token = Await _tokenManager.GetValidTokenAsync()
                Return Not String.IsNullOrEmpty(token)
            Catch ex As Exception
                _logger.LogWarning(ex, "Token validation failed")
                Return False
            End Try
        End Function

        Public Async Function RefreshTokenAsync() As Task(Of String) _
            Implements IAuthService.RefreshTokenAsync
            Await _tokenManager.ForceRefreshAsync()
            Return _tokenManager.CurrentToken
        End Function

    End Class

End Namespace
