Imports Microsoft.Extensions.DependencyInjection
Imports TopStepTrader.UI.Views

Namespace TopStepTrader.UI.ViewModels

    ''' <summary>
    ''' Singleton service that creates and caches one view per navigation section.
    ''' Uses IServiceScopeFactory to resolve Scoped dependencies (EF Core, repositories)
    ''' inside a proper scope rather than from the root container.
    ''' </summary>
    Public Class ViewModelLocator
        Implements IDisposable

        Private ReadOnly _scopeFactory As IServiceScopeFactory
        Private ReadOnly _scopes As New Dictionary(Of String, IServiceScope)()
        Private _disposed As Boolean = False

        Public Sub New(scopeFactory As IServiceScopeFactory)
            _scopeFactory = scopeFactory
        End Sub

        Private Function Resolve(Of T)(key As String) As T
            If Not _scopes.ContainsKey(key) Then
                _scopes(key) = _scopeFactory.CreateScope()
            End If
            Return _scopes(key).ServiceProvider.GetRequiredService(Of T)()
        End Function

        Public ReadOnly Property DashboardView As DashboardView
            Get
                Return Resolve(Of DashboardView)("Dashboard")
            End Get
        End Property

        Public ReadOnly Property MarketDataView As MarketDataView
            Get
                Return Resolve(Of MarketDataView)("Market")
            End Get
        End Property

        Public ReadOnly Property SignalsView As SignalsView
            Get
                Return Resolve(Of SignalsView)("Signals")
            End Get
        End Property

        Public ReadOnly Property OrderBookView As OrderBookView
            Get
                Return Resolve(Of OrderBookView)("Orders")
            End Get
        End Property

        Public ReadOnly Property RiskGuardView As RiskGuardView
            Get
                Return Resolve(Of RiskGuardView)("Risk")
            End Get
        End Property

        Public ReadOnly Property BacktestView As BacktestView
            Get
                Return Resolve(Of BacktestView)("Backtest")
            End Get
        End Property

        Public ReadOnly Property SettingsView As SettingsView
            Get
                Return Resolve(Of SettingsView)("Settings")
            End Get
        End Property

        Public ReadOnly Property AiTradingView As AiTradingView
            Get
                Return Resolve(Of AiTradingView)("AiTrading")
            End Get
        End Property

        Public ReadOnly Property TestTradeView As TestTradeView
            Get
                Return Resolve(Of TestTradeView)("TestTrade")
            End Get
        End Property

        Public ReadOnly Property SniperView As SniperView
            Get
                Return Resolve(Of SniperView)("Sniper")
            End Get
        End Property

        Public ReadOnly Property HydraView As Views.HydraView
            Get
                Return Resolve(Of Views.HydraView)("Hydra")
            End Get
        End Property

        Public ReadOnly Property CryptoJoeView As Views.CryptoJoeView
            Get
                Return Resolve(Of Views.CryptoJoeView)("CryptoJoe")
            End Get
        End Property

        Public ReadOnly Property ApiKeysView As Views.ApiKeysView
            Get
                Return Resolve(Of Views.ApiKeysView)("ApiKeys")
            End Get
        End Property

        Public ReadOnly Property QuantLabView As Views.QuantLabView
            Get
                Return Resolve(Of Views.QuantLabView)("QuantLab")
            End Get
        End Property

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                For Each scope In _scopes.Values
                    scope.Dispose()
                Next
                _scopes.Clear()
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
