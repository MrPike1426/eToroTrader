Imports Microsoft.Extensions.DependencyInjection
Imports TopStepTrader.API.Http
Imports TopStepTrader.API.Hubs
Imports TopStepTrader.API.RateLimiting

Namespace TopStepTrader.API

    Public Module ApiServiceExtensions

        ''' <summary>Register all eToro API services into the DI container.</summary>
        <System.Runtime.CompilerServices.Extension>
        Public Sub AddApiServices(services As IServiceCollection)

            ' Shared rate limiter — Singleton so all clients share the same window
            services.AddSingleton(Of RateLimiter)()

            ' eToro credentials provider — Singleton, holds static API keys
            services.AddSingleton(Of EToroCredentialsProvider)()

            ' Named HttpClient for eToro
            services.AddHttpClient("eToro",
                Sub(client)
                    client.Timeout = TimeSpan.FromSeconds(30)
                    client.DefaultRequestHeaders.Add("Accept", "application/json")
                End Sub)

            ' HTTP clients — Transient (stateless)
            services.AddTransient(Of AuthClient)()
            services.AddTransient(Of AccountClient)()
            services.AddTransient(Of ContractClient)()
            services.AddTransient(Of OrderClient)()
            services.AddTransient(Of HistoryClient)()

            ' WebSocket hub stubs — Singleton (will hold persistent connections when implemented)
            services.AddSingleton(Of MarketHubClient)()
            services.AddSingleton(Of UserHubClient)()

        End Sub

    End Module

End Namespace
