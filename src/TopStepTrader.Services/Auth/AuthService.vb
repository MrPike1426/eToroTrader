Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API
Imports TopStepTrader.Core.Interfaces

Namespace TopStepTrader.Services.Auth

    ''' <summary>
    ''' Implements IAuthService for eToro.
    ''' eToro authenticates via static API key headers — there is no login endpoint
    ''' and no JWT token lifecycle. This service validates that credentials are configured.
    ''' </summary>
    Public Class AuthService
        Implements IAuthService

        Private ReadOnly _credentials As EToroCredentialsProvider
        Private ReadOnly _logger As ILogger(Of AuthService)

        Public Sub New(credentials As EToroCredentialsProvider,
                       logger As ILogger(Of AuthService))
            _credentials = credentials
            _logger = logger
        End Sub

        Public ReadOnly Property CurrentToken As String Implements IAuthService.CurrentToken
            Get
                Return String.Empty  ' eToro uses header keys, not bearer tokens
            End Get
        End Property

        Public ReadOnly Property TokenExpiresAt As DateTimeOffset Implements IAuthService.TokenExpiresAt
            Get
                Return DateTimeOffset.MaxValue  ' Static keys do not expire
            End Get
        End Property

        Public ReadOnly Property IsAuthenticated As Boolean Implements IAuthService.IsAuthenticated
            Get
                Return _credentials.IsConfigured
            End Get
        End Property

        Public Function LoginAsync(userName As String, apiKey As String) As Task(Of String) _
            Implements IAuthService.LoginAsync
            ' eToro has no login endpoint — validate credentials are present
            If _credentials.IsConfigured Then
                _logger.LogInformation("eToro credentials are configured — no login required.")
            Else
                _logger.LogError("eToro credentials not configured. Set Api:ApiKey and Api:UserKey in appsettings.")
            End If
            Return Task.FromResult(If(_credentials.IsConfigured, "configured", String.Empty))
        End Function

        Public Function ValidateTokenAsync() As Task(Of Boolean) _
            Implements IAuthService.ValidateTokenAsync
            Return Task.FromResult(_credentials.IsConfigured)
        End Function

        Public Function RefreshTokenAsync() As Task(Of String) _
            Implements IAuthService.RefreshTokenAsync
            Return Task.FromResult(String.Empty)  ' No token refresh for eToro
        End Function

    End Class

End Namespace
