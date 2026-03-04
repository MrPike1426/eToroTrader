Imports Microsoft.Extensions.DependencyInjection
Imports TopStepTrader.API.Http
Imports TopStepTrader.API.Hubs
Imports TopStepTrader.API.RateLimiting

Namespace TopStepTrader.API

    Public Module ApiServiceExtensions

        ''' <summary>Register all ProjectX API services into the DI container.</summary>
        <System.Runtime.CompilerServices.Extension>
        Public Sub AddApiServices(services As IServiceCollection)

            ' Shared rate limiter — Singleton so all clients share the same window
            services.AddSingleton(Of RateLimiter)()

            ' TokenManager — Singleton to hold the cached token across the session
            services.AddSingleton(Of TokenManager)()

            ' Named HttpClient for ProjectX — base address and timeout set here
            services.AddHttpClient("ProjectX",
                Sub(client)
                    ' Base address is set per-call, but timeout is global
                    client.Timeout = TimeSpan.FromSeconds(30)
                    client.DefaultRequestHeaders.Add("Accept", "text/plain")
                End Sub)

            ' HTTP clients — Transient (stateless, get fresh HttpClient each time)
            services.AddTransient(Of AuthClient)()
            services.AddTransient(Of AccountClient)()
            services.AddTransient(Of ContractClient)()
            services.AddTransient(Of OrderClient)()
            services.AddTransient(Of HistoryClient)()

            ' SignalR hub clients — Singleton (maintain persistent connections)
            services.AddSingleton(Of MarketHubClient)()
            services.AddSingleton(Of UserHubClient)()

        End Sub

    End Module

End Namespace
