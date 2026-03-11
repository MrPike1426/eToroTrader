Imports System.Net.Http
Imports System.Text
Imports System.Text.Json
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.API.RateLimiting
Imports TopStepTrader.Core.Logging

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' Base class for all eToro HTTP clients.
    ''' Injects eToro auth headers (x-api-key, x-user-key, x-request-id) per request.
    ''' Supports GET, POST, and DELETE with rate limiting and JSON (de)serialisation.
    ''' </summary>
    Public MustInherit Class EToroHttpClientBase

        Protected ReadOnly HttpClient As HttpClient
        Protected ReadOnly Credentials As EToroCredentialsProvider
        Protected ReadOnly RateLimiter As RateLimiter
        Protected ReadOnly Logger As ILogger

        Private Shared ReadOnly JsonOptions As New JsonSerializerOptions With {
            .PropertyNameCaseInsensitive = True
        }

        Protected Sub New(httpClientFactory As IHttpClientFactory,
                          credentials As EToroCredentialsProvider,
                          rateLimiter As RateLimiter,
                          logger As ILogger)
            HttpClient = httpClientFactory.CreateClient("eToro")
            Me.Credentials = credentials
            Me.RateLimiter = rateLimiter
            Me.Logger = logger
        End Sub

        ''' <summary>Stamps every outgoing request with the three required eToro headers.</summary>
        Private Sub AddEToroHeaders(request As HttpRequestMessage)
            request.Headers.Remove("x-api-key")
            request.Headers.Remove("x-user-key")
            request.Headers.Remove("x-request-id")
            request.Headers.Add("x-api-key", Credentials.ApiKey)
            request.Headers.Add("x-user-key", Credentials.UserKey)
            request.Headers.Add("x-request-id", Guid.NewGuid().ToString())
        End Sub

        ''' <summary>Authenticated, rate-limited POST with JSON body.</summary>
        Protected Async Function PostAsync(Of TRequest, TResponse)(
            endpoint As String,
            request As TRequest,
            Optional cancel As CancellationToken = Nothing) As Task(Of TResponse)

            Await RateLimiter.WaitForGeneralSlotAsync(cancel)

            Dim json = JsonSerializer.Serialize(request)
            Logger.LogDebug("POST {Endpoint} → {Body}", endpoint, json)
            Try : DebugLog.Log($"POST {endpoint} → {json}") : Catch : End Try

            Dim httpRequest = New HttpRequestMessage(HttpMethod.Post, endpoint) With {
                .Content = New StringContent(json, Encoding.UTF8, "application/json")
            }
            AddEToroHeaders(httpRequest)

            Dim httpResponse = Await HttpClient.SendAsync(httpRequest, cancel)

            If httpResponse.StatusCode = Net.HttpStatusCode.TooManyRequests Then
                Logger.LogWarning("Rate limit hit on {Endpoint}, backing off 5s", endpoint)
                Await Task.Delay(5000, cancel)
                Dim retryReq = New HttpRequestMessage(HttpMethod.Post, endpoint) With {
                    .Content = New StringContent(json, Encoding.UTF8, "application/json")
                }
                AddEToroHeaders(retryReq)
                httpResponse = Await HttpClient.SendAsync(retryReq, cancel)
            End If

            Return Await ReadResponseAsync(Of TResponse)(httpResponse, endpoint, cancel)
        End Function

        ''' <summary>Authenticated, rate-limited GET.</summary>
        Protected Async Function GetAsync(Of TResponse)(
            endpoint As String,
            Optional cancel As CancellationToken = Nothing) As Task(Of TResponse)

            Await RateLimiter.WaitForGeneralSlotAsync(cancel)

            Logger.LogDebug("GET {Endpoint}", endpoint)
            Try : DebugLog.Log($"GET {endpoint}") : Catch : End Try

            Dim httpRequest = New HttpRequestMessage(HttpMethod.Get, endpoint)
            AddEToroHeaders(httpRequest)

            Dim httpResponse = Await HttpClient.SendAsync(httpRequest, cancel)

            If httpResponse.StatusCode = Net.HttpStatusCode.TooManyRequests Then
                Logger.LogWarning("Rate limit hit on {Endpoint}, backing off 5s", endpoint)
                Await Task.Delay(5000, cancel)
                Dim retryReq = New HttpRequestMessage(HttpMethod.Get, endpoint)
                AddEToroHeaders(retryReq)
                httpResponse = Await HttpClient.SendAsync(retryReq, cancel)
            End If

            Return Await ReadResponseAsync(Of TResponse)(httpResponse, endpoint, cancel)
        End Function

        ''' <summary>Authenticated, rate-limited PUT with JSON body (used to edit open positions).</summary>
        Protected Async Function PutAsync(Of TRequest, TResponse)(
            endpoint As String,
            request As TRequest,
            Optional cancel As CancellationToken = Nothing) As Task(Of TResponse)

            Await RateLimiter.WaitForGeneralSlotAsync(cancel)

            Dim json = JsonSerializer.Serialize(request)
            Logger.LogDebug("PUT {Endpoint} → {Body}", endpoint, json)
            Try : DebugLog.Log($"PUT {endpoint} → {json}") : Catch : End Try

            Dim httpRequest = New HttpRequestMessage(HttpMethod.Put, endpoint) With {
                .Content = New StringContent(json, Encoding.UTF8, "application/json")
            }
            AddEToroHeaders(httpRequest)

            Dim httpResponse = Await HttpClient.SendAsync(httpRequest, cancel)

            If httpResponse.StatusCode = Net.HttpStatusCode.TooManyRequests Then
                Logger.LogWarning("Rate limit hit on {Endpoint}, backing off 5s", endpoint)
                Await Task.Delay(5000, cancel)
                Dim retryReq = New HttpRequestMessage(HttpMethod.Put, endpoint) With {
                    .Content = New StringContent(json, Encoding.UTF8, "application/json")
                }
                AddEToroHeaders(retryReq)
                httpResponse = Await HttpClient.SendAsync(retryReq, cancel)
            End If

            Return Await ReadResponseAsync(Of TResponse)(httpResponse, endpoint, cancel)
        End Function

        ''' <summary>Authenticated, rate-limited DELETE (used to cancel pending orders).</summary>
        Protected Async Function DeleteAsync(Of TResponse)(
            endpoint As String,
            Optional cancel As CancellationToken = Nothing) As Task(Of TResponse)

            Await RateLimiter.WaitForGeneralSlotAsync(cancel)

            Logger.LogDebug("DELETE {Endpoint}", endpoint)
            Try : DebugLog.Log($"DELETE {endpoint}") : Catch : End Try

            Dim httpRequest = New HttpRequestMessage(HttpMethod.Delete, endpoint)
            AddEToroHeaders(httpRequest)

            Dim httpResponse = Await HttpClient.SendAsync(httpRequest, cancel)
            Return Await ReadResponseAsync(Of TResponse)(httpResponse, endpoint, cancel)
        End Function

        Private Async Function ReadResponseAsync(Of TResponse)(
            response As HttpResponseMessage,
            endpoint As String,
            cancel As CancellationToken) As Task(Of TResponse)

            Dim body = Await response.Content.ReadAsStringAsync(cancel)

            If Not response.IsSuccessStatusCode Then
                Logger.LogError("eToro API {Status} on {Endpoint}. Response body: {Body}",
                                CInt(response.StatusCode), endpoint, body)
                Throw New HttpRequestException(
                    $"eToro {CInt(response.StatusCode)} {response.ReasonPhrase} — {body}",
                    Nothing, response.StatusCode)
            End If

            Logger.LogDebug("Response ← {Endpoint} {Body}", endpoint, body)
            Try : DebugLog.Log($"Response {endpoint} ← {body}") : Catch : End Try

            Dim result = JsonSerializer.Deserialize(Of TResponse)(body, JsonOptions)
            If result Is Nothing Then
                Throw New InvalidOperationException($"Null deserialization from {endpoint}")
            End If
            Return result
        End Function

    End Class

End Namespace
