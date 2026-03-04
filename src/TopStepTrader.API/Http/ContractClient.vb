Imports System.Net.Http
Imports System.Net
Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.API.Models.Responses
Imports TopStepTrader.API.RateLimiting
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' Searches for eToro instruments by ticker symbol.
    ''' GET /api/v1/market-data/search?internalSymbolFull={symbol}
    ''' Instrument IDs are immutable — cache results locally after first lookup.
    ''' </summary>
    Public Class ContractClient
        Inherits EToroHttpClientBase

        Private ReadOnly _settings As ApiSettings

        Public Sub New(options As IOptions(Of ApiSettings),
                       httpClientFactory As IHttpClientFactory,
                       credentials As EToroCredentialsProvider,
                       rateLimiter As RateLimiter,
                       logger As ILogger(Of ContractClient))
            MyBase.New(httpClientFactory, credentials, rateLimiter, logger)
            _settings = options.Value
        End Sub

        ''' <summary>
        ''' Resolves a ticker symbol to its eToro instrumentId.
        ''' Filters by internalSymbolFull for an exact match.
        ''' </summary>
        Public Async Function SearchInstrumentAsync(
            symbol As String,
            Optional cancel As CancellationToken = Nothing) As Task(Of InstrumentSearchResponse)

            Dim encoded = WebUtility.UrlEncode(symbol)
            Dim endpoint = $"{_settings.BaseUrl}/api/v1/market-data/search?internalSymbolFull={encoded}"
            Return Await GetAsync(Of InstrumentSearchResponse)(endpoint, cancel)
        End Function

        ''' <summary>
        ''' Legacy helper — maps instrument search to ContractAvailableResponse so existing
        ''' ContractMetadataService code requires minimal changes.
        ''' </summary>
        Public Async Function GetAvailableContractsAsync(
            Optional searchText As String = "",
            Optional cancel As CancellationToken = Nothing) As Task(Of ContractAvailableResponse)

            Dim response = Await SearchInstrumentAsync(searchText, cancel)
            Dim contracts = response.Items.Select(Function(i) New ContractDto With {
                .ContractId = i.InternalSymbolFull,
                .Name = i.DisplayName,
                .Description = i.DisplayName,
                .InstrumentId = i.InstrumentId
            }).ToList()

            Return New ContractAvailableResponse With {
                .Success = True,
                .Contracts = contracts
            }
        End Function

    End Class

End Namespace
