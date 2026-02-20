Imports System.Net.Http
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.API.Models.Requests
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' Fetches historical price bars. Rate limited to 50 req/30s.
    ''' All calls go through the history slot on RateLimiter.
    ''' </summary>
    Public Class HistoryClient
        Inherits ProjectXHttpClientBase

        Private ReadOnly _settings As ApiSettings

        ''' <summary>ProjectX bar unit codes → minutes mapping</summary>
        Public Shared ReadOnly BarUnitMap As New Dictionary(Of Integer, String) From {
            {1, "1 min"}, {2, "5 min"}, {3, "15 min"},
            {4, "30 min"}, {5, "1 hr"}, {6, "1 day"}
        }

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       tokenManager As TokenManager,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of HistoryClient))
            MyBase.New(httpClientFactory, tokenManager, rateLimiter, logger)
            _settings = options.Value
        End Sub

        ''' <summary>
        ''' Retrieve historical bars. Uses the history-specific rate limit slot.
        ''' </summary>
        ''' <param name="contractId">Numeric contract ID from Contract/available</param>
        ''' <param name="unit">1=1min, 2=5min, 3=15min, 4=30min, 5=1hr, 6=1day</param>
        ''' <param name="unitsBack">Number of bars to fetch (max ~500 per call)</param>
        Public Function RetrieveBarsAsync(contractId As Integer,
                                          unit As Integer,
                                          unitsBack As Integer,
                                          Optional startTime As DateTimeOffset? = Nothing,
                                          Optional endTime As DateTimeOffset? = Nothing,
                                          Optional cancel As CancellationToken = Nothing) As Task(Of BarResponse)

            Dim request = New RetrieveBarsRequest With {
                .ContractId = contractId,
                .Unit = unit,
                .UnitsBack = unitsBack,
                .StartTime = If(startTime.HasValue, startTime.Value.ToString("O"), Nothing),
                .EndTime = If(endTime.HasValue, endTime.Value.ToString("O"), Nothing)
            }
            Dim endpoint = $"{_settings.RestBaseUrl}/api/History/retrieveBars"

            ' useHistoryLimit:=True to apply the stricter 50/30s window
            Return PostAsync(Of RetrieveBarsRequest, BarResponse)(endpoint, request,
                                                                   useHistoryLimit:=True,
                                                                   cancel:=cancel)
        End Function

    End Class

End Namespace
