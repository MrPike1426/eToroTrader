Imports System.Net.Http
Imports System.Net.Http.Headers
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API.RateLimiting
Imports TopStepTrader.Core.Logging

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' Base class for all ProjectX HTTP clients.
    ''' Handles: auth header injection, rate limiting, JSON serialisation, error mapping.
    ''' </summary>
    Public MustInherit Class ProjectXHttpClientBase

        Protected ReadOnly HttpClient As HttpClient
        Protected ReadOnly TokenManager As TokenManager
        Protected ReadOnly RateLimiter As RateLimiter
        Protected ReadOnly Logger As ILogger

        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
            .PropertyNameCaseInsensitive = True
        }

        Protected Sub New(httpClientFactory As IHttpClientFactory,
                          tokenManager As TokenManager,
                          rateLimiter As RateLimiter,
                          logger As ILogger)
            HttpClient = httpClientFactory.CreateClient("ProjectX")
            Me.TokenManager = tokenManager
            Me.RateLimiter = rateLimiter
            Me.Logger = logger
        End Sub

        ''' <summary>POST with JSON body, rate-limited and authenticated.</summary>
        Protected Async Function PostAsync(Of TRequest, TResponse)(
            endpoint As String,
            request As TRequest,
            Optional useHistoryLimit As Boolean = False,
            Optional cancel As CancellationToken = Nothing) As Task(Of TResponse)

            ' Rate limit gate
            If useHistoryLimit Then
                Await RateLimiter.WaitForHistorySlotAsync(cancel)
            Else
                Await RateLimiter.WaitForGeneralSlotAsync(cancel)
            End If

            ' Inject fresh token
            Dim token = Await TokenManager.GetValidTokenAsync(cancel)
            HttpClient.DefaultRequestHeaders.Authorization =
                New AuthenticationHeaderValue("Bearer", token)

            Dim json = JsonSerializer.Serialize(request)
            Dim content = New StringContent(json, Encoding.UTF8, "application/json")

            Logger.LogDebug("POST {Endpoint} → {Body}", endpoint, json)
            Try
                DebugLog.Log($"POST {endpoint} → {json}")
            Catch
            End Try

            Dim httpResponse = Await HttpClient.PostAsync(endpoint, content, cancel)

            If httpResponse.StatusCode = Net.HttpStatusCode.TooManyRequests Then
                Logger.LogWarning("Rate limit hit on {Endpoint}, backing off 5s", endpoint)
                Await Task.Delay(5000, cancel)
                ' Retry once after back-off (Polly handles additional retries)
                httpResponse = Await HttpClient.PostAsync(endpoint, content, cancel)
            End If

            httpResponse.EnsureSuccessStatusCode()

            Dim responseJson = Await httpResponse.Content.ReadAsStringAsync(cancel)
            Logger.LogDebug("Response from {Endpoint} ← {Body}", endpoint, responseJson)
            Try
                DebugLog.Log($"Response {endpoint} ← {responseJson}")
            Catch
            End Try

            Dim result = JsonSerializer.Deserialize(Of TResponse)(responseJson, JsonOptions)
            If result Is Nothing Then
                Throw New InvalidOperationException($"Null response from {endpoint}")
            End If

            Return result
        End Function

    End Class

End Namespace
