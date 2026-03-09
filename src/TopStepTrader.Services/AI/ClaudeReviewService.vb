Imports System.Net.Http
Imports System.Net.Http.Json
Imports System.Text.Json.Serialization
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.Services.AI

    ''' <summary>
    ''' Sends a strategy definition to the Anthropic Claude API and returns
    ''' plain-text improvement suggestions. Uses the model configured in ClaudeSettings
    ''' (defaults to claude-haiku for cost efficiency).
    ''' </summary>
    Public Class ClaudeReviewService

        Private ReadOnly _settings As ClaudeSettings
        Private ReadOnly _apiKeyStore As IApiKeyStore
        Private ReadOnly _logger As ILogger(Of ClaudeReviewService)
        Private Shared ReadOnly _http As New HttpClient()

        Private Const AnthropicMessagesUrl As String = "https://api.anthropic.com/v1/messages"
        Private Const AnthropicVersion As String = "2023-06-01"

        Private Const SystemPrompt As String =
            "You are an expert futures and crypto trading strategy advisor. " &
            "Analyze the following trading strategy and give 2-4 concise, " &
            "actionable improvement suggestions covering: entry logic, exit " &
            "placement (take-profit / stop-loss), risk sizing, and timing " &
            "(session hours, news events). Be specific about numbers where " &
            "possible. Keep your total response under 200 words."

        Private Const ConfidenceSystemPrompt As String =
            "You are an experienced micro-futures day trader. Given a contract symbol, provide a " &
            "brief confidence assessment in 3-4 bullet points covering: (1) what macro factors " &
            "typically drive this instrument, (2) best session windows (London/NY overlap etc.), " &
            "(3) any known seasonal or structural tendencies right now, and (4) your overall bias " &
            "(🟢 Long / 🔴 Short / 🟡 Neutral) with one-sentence rationale. " &
            "Note your knowledge has a cutoff date — conduct live research against technical and sentiment analysis. " &
            "Keep your total response under 150 words."

        Public Sub New(options As IOptions(Of ClaudeSettings), apiKeyStore As IApiKeyStore, logger As ILogger(Of ClaudeReviewService))
            _settings = options.Value
            _apiKeyStore = apiKeyStore
            _logger = logger
        End Sub

        ''' <summary>
        ''' Resolves the active Claude API key — prefers the key stored on the API Keys
        ''' page (local apikeys.json) and falls back to appsettings.json for backward
        ''' compatibility with existing deployments.
        ''' </summary>
        Private Function ResolveApiKey() As String
            Dim stored = _apiKeyStore.Load().ClaudeApiKey
            If Not String.IsNullOrWhiteSpace(stored) Then Return stored
            Return _settings.ApiKey
        End Function

        ''' <summary>
        ''' Calls Claude to review the strategy. Returns suggestion text, or a
        ''' user-friendly message if the API key is not yet configured.
        ''' </summary>
        Public Async Function ReviewStrategyAsync(strategy As StrategyDefinition,
                                                   Optional cancel As CancellationToken = Nothing) As Task(Of String)
            Dim apiKey = ResolveApiKey()
            If String.IsNullOrWhiteSpace(apiKey) Then
                Return "⚠️  Claude API key not configured — add it on the API Keys page."
            End If

            Dim userMessage = BuildUserMessage(strategy)

            Try
                Dim requestBody = New ClaudeRequest With {
                    .Model = _settings.Model,
                    .MaxTokens = _settings.MaxTokens,
                    .System = SystemPrompt,
                    .Messages = New List(Of ClaudeMessage) From {
                        New ClaudeMessage With {.Role = "user", .Content = userMessage}
                    }
                }

                Using request As New HttpRequestMessage(HttpMethod.Post, AnthropicMessagesUrl)
                    request.Headers.Add("x-api-key", apiKey)
                    request.Headers.Add("anthropic-version", AnthropicVersion)
                    request.Content = JsonContent.Create(requestBody)

                    Dim response = Await _http.SendAsync(request, cancel)

                    If Not response.IsSuccessStatusCode Then
                        Dim errorBody = Await response.Content.ReadAsStringAsync(cancel)
                        _logger.LogWarning("Claude API returned {Status}: {Body}", response.StatusCode, errorBody)
                        Return $"⚠️  Claude API error {CInt(response.StatusCode)} — check your API key on the API Keys page."
                    End If

                    Dim result = Await response.Content.ReadFromJsonAsync(Of ClaudeResponse)(cancellationToken:=cancel)
                    Dim text = result?.Content?.FirstOrDefault()?.Text

                    If String.IsNullOrWhiteSpace(text) Then
                        Return "⚠️  Claude returned an empty response. Please try again."
                    End If

                    Return text

                End Using

            Catch ex As TaskCanceledException
                Return "⚠️  Request timed out. Check your internet connection and try again."
            Catch ex As Exception
                _logger.LogError(ex, "ClaudeReviewService error")
                Return $"⚠️  Unexpected error: {ex.Message}"
            End Try
        End Function

        ''' <summary>
        ''' Asks Claude for a quick confidence / sentiment check on the given contract.
        ''' Returns bullet-point market context — does NOT access live data.
        ''' </summary>
        Public Async Function ConfidenceCheckAsync(contractId As String,
                                                    Optional cancel As CancellationToken = Nothing) As Task(Of String)
            Dim apiKey = ResolveApiKey()
            If String.IsNullOrWhiteSpace(apiKey) Then
                Return "⚠️  Claude API key not configured — add it on the API Keys page."
            End If

            Dim userMessage = $"Contract: {contractId}{Environment.NewLine}" &
                              "Provide your confidence assessment for trading this instrument right now."

            Try
                Dim requestBody = New ClaudeRequest With {
                    .Model = _settings.Model,
                    .MaxTokens = _settings.MaxTokens,
                    .System = ConfidenceSystemPrompt,
                    .Messages = New List(Of ClaudeMessage) From {
                        New ClaudeMessage With {.Role = "user", .Content = userMessage}
                    }
                }

                Using request As New HttpRequestMessage(HttpMethod.Post, AnthropicMessagesUrl)
                    request.Headers.Add("x-api-key", apiKey)
                    request.Headers.Add("anthropic-version", AnthropicVersion)
                    request.Content = JsonContent.Create(requestBody)

                    Dim response = Await _http.SendAsync(request, cancel)

                    If Not response.IsSuccessStatusCode Then
                        Dim errorBody = Await response.Content.ReadAsStringAsync(cancel)
                        _logger.LogWarning("Claude API returned {Status}: {Body}", response.StatusCode, errorBody)
                        Return $"⚠️  Claude API error {CInt(response.StatusCode)} — check your API key on the API Keys page."
                    End If

                    Dim result = Await response.Content.ReadFromJsonAsync(Of ClaudeResponse)(cancellationToken:=cancel)
                    Dim text = result?.Content?.FirstOrDefault()?.Text

                    If String.IsNullOrWhiteSpace(text) Then
                        Return "⚠️  Claude returned an empty response. Please try again."
                    End If

                    Return text

                End Using

            Catch ex As TaskCanceledException
                Return "⚠️  Request timed out. Check your internet connection and try again."
            Catch ex As Exception
                _logger.LogError(ex, "ClaudeReviewService.ConfidenceCheckAsync error")
                Return $"⚠️  Unexpected error: {ex.Message}"
            End Try
        End Function

        Private Shared Function BuildUserMessage(strategy As StrategyDefinition) As String
            Dim sb As New System.Text.StringBuilder()
            sb.AppendLine("STRATEGY TO REVIEW:")
            sb.AppendLine($"  Name:        {strategy.Name}")
            sb.AppendLine($"  Contract:    {strategy.ContractId}")
            sb.AppendLine($"  Timeframe:   {strategy.TimeframeMinutes}-minute bars")
            sb.AppendLine($"  Duration:    {strategy.DurationHours} hours")
            sb.AppendLine($"  Indicator:   {strategy.Indicator} (period={strategy.IndicatorPeriod}, mult={strategy.IndicatorMultiplier})")
            sb.AppendLine($"  Condition:   {strategy.Condition}")
            sb.AppendLine($"  Long entry:  {strategy.GoLongWhenBelowBands}")
            sb.AppendLine($"  Short entry: {strategy.GoShortWhenAboveBands}")
            sb.AppendLine($"  Take Profit: {If(strategy.TakeProfitTicks > 0, $"{strategy.TakeProfitTicks} ticks", "None")}")
            sb.AppendLine($"  Stop Loss:   {If(strategy.StopLossTicks > 0, $"{strategy.StopLossTicks} ticks", "None")}")
            sb.AppendLine($"  Quantity:    {strategy.Quantity} contract(s)")
            sb.AppendLine($"  Capital Risk: ${strategy.CapitalAtRisk:F2}")
            If Not String.IsNullOrWhiteSpace(strategy.RawDescription) Then
                sb.AppendLine()
                sb.AppendLine("ORIGINAL DESCRIPTION:")
                sb.AppendLine(strategy.RawDescription)
            End If
            Return sb.ToString()
        End Function

        ' ── JSON DTOs ─────────────────────────────────────────────────────────────

        Private Class ClaudeRequest
            <JsonPropertyName("model")>
            Public Property Model As String

            <JsonPropertyName("max_tokens")>
            Public Property MaxTokens As Integer

            <JsonPropertyName("system")>
            Public Property System As String

            <JsonPropertyName("messages")>
            Public Property Messages As List(Of ClaudeMessage)
        End Class

        Private Class ClaudeMessage
            <JsonPropertyName("role")>
            Public Property Role As String

            <JsonPropertyName("content")>
            Public Property Content As String
        End Class

        Private Class ClaudeResponse
            <JsonPropertyName("content")>
            Public Property Content As List(Of ClaudeContent)
        End Class

        Private Class ClaudeContent
            <JsonPropertyName("text")>
            Public Property Text As String
        End Class

    End Class

End Namespace
