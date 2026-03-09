Imports Microsoft.Extensions.DependencyInjection
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Data.Repositories
Imports TopStepTrader.Services.AI
Imports TopStepTrader.Services.Auth
Imports TopStepTrader.Services.Background
Imports TopStepTrader.Services.Backtest
Imports TopStepTrader.Services.Feedback
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

            ' ── API key store — Singleton: one file-backed store for the session lifetime
            services.AddSingleton(Of IApiKeyStore, ApiKeyStore)()

            ' ── Auth
            services.AddSingleton(Of IAuthService, AuthService)()

            ' ── Account
            services.AddScoped(Of IAccountService, AccountService)()
            services.AddScoped(Of IBalanceHistoryService, BalanceHistoryService)()

            ' ── Market
            services.AddScoped(Of MarketDataService)()
            services.AddScoped(Of IMarketDataService)(Function(sp) sp.GetRequiredService(Of MarketDataService)())
            services.AddScoped(Of BarIngestionService)()
            ' TICKET-006: bar download + caching for the Backtest page
            services.AddScoped(Of IBarCollectionService, BarCollectionService)()

            ' ── Signals
            services.AddScoped(Of ISignalService, SignalService)()

            ' ── Risk guard — Singleton so halt state persists for the session lifetime
            services.AddSingleton(Of IRiskGuardService, RiskGuardService)()

            ' ── Trading
            services.AddScoped(Of IOrderService, OrderService)()
            services.AddScoped(Of AutoExecutionService)()
            services.AddScoped(Of TrendAnalysisService)()

            ' ── AI-Assisted Trading
            services.AddScoped(Of StrategyParserService)()
            services.AddTransient(Of StrategyExecutionEngine)()
            ' CryptoJoe-specific engine: BUY-only + 100% confidence override.
            ' Wired exclusively through CryptoJoeViewModel — other pages remain on StrategyExecutionEngine.
            services.AddTransient(Of CryptoStrategyExecutionEngine)()
            services.AddScoped(Of ClaudeReviewService)()

            ' ── Backtest
            services.AddScoped(Of IBacktestService, BacktestEngine)()

            ' ── ML Feedback Loop (Phase 7)
            ' OutcomeTracker is Scoped — OutcomeMonitorWorker creates a scope per tick
            services.AddScoped(Of OutcomeTracker)()
            ' ModelTrainingService is Scoped — has Scoped repo dependencies
            services.AddScoped(Of IModelTrainingService, ModelTrainingService)()

            ' ── Background workers
            services.AddSingleton(Of BarIngestionWorker)()
            services.AddSingleton(Of TokenRefreshWorker)()
            services.AddSingleton(Of SignalGenerationWorker)()
            ' OutcomeMonitorWorker uses IServiceScopeFactory — safe as Singleton
            services.AddSingleton(Of OutcomeMonitorWorker)()

            services.AddHostedService(Function(sp) sp.GetRequiredService(Of TokenRefreshWorker)())
            services.AddHostedService(Function(sp) sp.GetRequiredService(Of BarIngestionWorker)())
            services.AddHostedService(Function(sp) sp.GetRequiredService(Of SignalGenerationWorker)())
            services.AddHostedService(Function(sp) sp.GetRequiredService(Of OutcomeMonitorWorker)())

        End Sub

    End Module

End Namespace
