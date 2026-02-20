Imports Microsoft.Extensions.DependencyInjection
Imports Microsoft.Extensions.Hosting
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.Data.Repositories
Imports TopStepTrader.Services.Auth
Imports TopStepTrader.Services.Backtest
Imports TopStepTrader.Services.Background
Imports TopStepTrader.Services.Market
Imports TopStepTrader.Services.Risk
Imports TopStepTrader.Services.Signals
Imports TopStepTrader.Services.Trading

Namespace TopStepTrader.Services

    Public Module ServicesExtensions

        <System.Runtime.CompilerServices.Extension>
        Public Sub AddApplicationServices(services As IServiceCollection)

            ' ── Repositories (Data layer, registered as Scoped by DataServiceExtensions)
            ' BarRepository, SignalRepository, OrderRepository registered by AddDataServices()
            ' BacktestRepository not yet registered there — add it here as Scoped
            services.AddScoped(Of BacktestRepository)()

            ' ── Auth
            services.AddSingleton(Of IAuthService, AuthService)()

            ' ── Account
            services.AddScoped(Of IAccountService, AccountService)()

            ' ── Market
            services.AddScoped(Of MarketDataService)()
            services.AddScoped(Of IMarketDataService)(Function(sp) sp.GetRequiredService(Of MarketDataService)())
            services.AddScoped(Of BarIngestionService)()

            ' ── Signals
            services.AddScoped(Of ISignalService, SignalService)()

            ' ── Risk guard — Singleton so halt state persists for the session lifetime
            services.AddSingleton(Of IRiskGuardService, RiskGuardService)()

            ' ── Trading
            services.AddScoped(Of IOrderService, OrderService)()
            services.AddScoped(Of AutoExecutionService)()

            ' ── Backtest
            services.AddScoped(Of IBacktestService, BacktestEngine)()

            ' ── Background workers
            services.AddSingleton(Of BarIngestionWorker)()
            services.AddSingleton(Of TokenRefreshWorker)()
            services.AddSingleton(Of SignalGenerationWorker)()

            services.AddHostedService(Function(sp) sp.GetRequiredService(Of TokenRefreshWorker)())
            services.AddHostedService(Function(sp) sp.GetRequiredService(Of BarIngestionWorker)())
            services.AddHostedService(Function(sp) sp.GetRequiredService(Of SignalGenerationWorker)())

        End Sub

    End Module

End Namespace
