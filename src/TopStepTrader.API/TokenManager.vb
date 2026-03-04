Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API

    ''' <summary>
    ''' Manages the ProjectX JWT session token.
    ''' Thread-safe: uses SemaphoreSlim to prevent concurrent refresh races.
    ''' Token is proactively refreshed N minutes before expiry.
    ''' </summary>
    Public Class TokenManager

        Private ReadOnly _settings As ApiSettings
        Private ReadOnly _httpClient As HttpClient
        Private ReadOnly _logger As ILogger(Of TokenManager)
        Private ReadOnly _semaphore As New SemaphoreSlim(1, 1)

        Private _token As String = String.Empty
        Private _expiresAt As DateTimeOffset = DateTimeOffset.MinValue

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       logger As ILogger(Of TokenManager))
            _settings = options.Value
            _httpClient = httpClientFactory.CreateClient("ProjectX")
            _logger = logger
        End Sub

        ''' <summary>Returns a valid token, refreshing automatically if needed.</summary>
        Public Async Function GetValidTokenAsync(Optional cancel As CancellationToken = Nothing) As Task(Of String)
            ' Fast path: token still valid (no lock needed for read)
            Dim refreshMinutes = _settings.TokenRefreshMinutesBeforeExpiry
            If Not String.IsNullOrEmpty(_token) AndAlso
               DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-refreshMinutes) Then
                Return _token
            End If

            ' Slow path: need to refresh — acquire lock
            Await _semaphore.WaitAsync(cancel)
            Try
                ' Double-check after acquiring lock
                If Not String.IsNullOrEmpty(_token) AndAlso
                   DateTimeOffset.UtcNow < _expiresAt.AddMinutes(-refreshMinutes) Then
                    Return _token
                End If

                Await RefreshTokenInternalAsync(cancel)
                Return _token
            Finally
                _semaphore.Release()
            End Try
        End Function

        Public ReadOnly Property CurrentToken As String
            Get
                Return _token
            End Get
        End Property

        Public ReadOnly Property TokenExpiresAt As DateTimeOffset
            Get
                Return _expiresAt
            End Get
        End Property

        Public ReadOnly Property IsAuthenticated As Boolean
            Get
                Return Not String.IsNullOrEmpty(_token) AndAlso DateTimeOffset.UtcNow < _expiresAt
            End Get
        End Property

        ''' <summary>Force a token refresh (called by TokenRefreshWorker background service).</summary>
        Public Async Function ForceRefreshAsync(Optional cancel As CancellationToken = Nothing) As Task
            Await _semaphore.WaitAsync(cancel)
            Try
                Await RefreshTokenInternalAsync(cancel)
            Finally
                _semaphore.Release()
            End Try
        End Function

        Private Async Function RefreshTokenInternalAsync(cancel As CancellationToken) As Task
            _logger.LogInformation("Refreshing ProjectX session token for user {User}", _settings.UserName)

            Dim request = New LoginKeyRequest With {
                .UserName = _settings.UserName,
                .ApiKey = _settings.ApiKey
            }

            Dim json = JsonSerializer.Serialize(request)
            Dim content = New StringContent(json, Encoding.UTF8, "application/json")

            Dim url = $"{_settings.RestBaseUrl}/api/Auth/loginKey"
            Dim response = Await _httpClient.PostAsync(url, content, cancel)
            response.EnsureSuccessStatusCode()

            Dim responseJson = Await response.Content.ReadAsStringAsync(cancel)
            Dim authResponse = JsonSerializer.Deserialize(Of AuthResponse)(responseJson)

            If authResponse Is Nothing OrElse Not authResponse.Success Then
                Dim err = If(authResponse?.ErrorMessage, "Unknown error")
                _logger.LogError("Token refresh failed: {Error} (code {Code})", err, authResponse?.ErrorCode)
                Throw New InvalidOperationException($"Authentication failed: {err}")
            End If

            _token = authResponse.Token
            ' ProjectX tokens are valid for 24 hours
            _expiresAt = DateTimeOffset.UtcNow.AddHours(24)
            _logger.LogInformation("Token refreshed successfully. Expires at {Expiry:O}", _expiresAt)
        End Function

    End Class

End Namespace
