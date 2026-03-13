Imports System.Windows
Imports System.Windows.Controls
Imports System.Windows.Media
Imports TopStepTrader.UI.ViewModels

Namespace TopStepTrader.UI

    Partial Public Class MainWindow
        Inherits Window

        Private ReadOnly _viewModelLocator As ViewModelLocator

        Public Sub New(viewModelLocator As ViewModelLocator)
            InitializeComponent()
            _viewModelLocator = viewModelLocator
            DataContext = Me
            NavigateTo("Dashboard")
        End Sub

        Public ReadOnly Property ConnectionStatus As String
            Get
                Return "● Connected"
            End Get
        End Property

        Public ReadOnly Property ConnectionStatusBrush As SolidColorBrush
            Get
                Return New SolidColorBrush(Color.FromRgb(39, 174, 96))
            End Get
        End Property

        Private Sub NavButton_Click(sender As Object, e As RoutedEventArgs)
            Dim btn = CType(sender, Button)
            NavigateTo(btn.Tag.ToString())
        End Sub

        Private Sub NavigateTo(section As String)
            Select Case section
                Case "Dashboard"
                    MainContent.Content = _viewModelLocator.DashboardView
                Case "Market"
                    MainContent.Content = _viewModelLocator.MarketDataView
                Case "Signals"
                    MainContent.Content = _viewModelLocator.SignalsView
                Case "Orders"
                    MainContent.Content = _viewModelLocator.OrderBookView
                Case "Risk"
                    MainContent.Content = _viewModelLocator.RiskGuardView
                Case "TestTrade"
                    MainContent.Content = _viewModelLocator.TestTradeView
                Case "AiTrading"
                    Dim aiView = _viewModelLocator.AiTradingView
                    Dim dashVm = TryCast(_viewModelLocator.DashboardView.DataContext, DashboardViewModel)
                    Dim aiVm = TryCast(aiView.DataContext, AiTradingViewModel)
                    If dashVm?.SelectedAccount IsNot Nothing AndAlso aiVm IsNot Nothing Then
                        aiVm.SyncDashboardAccount(dashVm.SelectedAccount)
                    End If
                    MainContent.Content = aiView
                Case "Backtest"
                    MainContent.Content = _viewModelLocator.BacktestView
                Case "QuantLab"
                    MainContent.Content = _viewModelLocator.QuantLabView
                Case "Sniper"
                    MainContent.Content = _viewModelLocator.SniperView
                Case "Hydra"
                    Dim hydraView = _viewModelLocator.HydraView
                    Dim dashVm = TryCast(_viewModelLocator.DashboardView.DataContext, DashboardViewModel)
                    Dim hydraVm = TryCast(hydraView.DataContext, HydraViewModel)
                    If dashVm?.SelectedAccount IsNot Nothing AndAlso hydraVm IsNot Nothing Then
                        hydraVm.SyncDashboardAccount(dashVm.SelectedAccount)
                    End If
                    MainContent.Content = hydraView
                Case "CryptoJoe"
                    Dim cryptoJoeView = _viewModelLocator.CryptoJoeView
                    Dim dashVm2 = TryCast(_viewModelLocator.DashboardView.DataContext, DashboardViewModel)
                    Dim cryptoJoeVm = TryCast(cryptoJoeView.DataContext, CryptoJoeViewModel)
                    If dashVm2?.SelectedAccount IsNot Nothing AndAlso cryptoJoeVm IsNot Nothing Then
                        cryptoJoeVm.SyncDashboardAccount(dashVm2.SelectedAccount)
                    End If
                    MainContent.Content = cryptoJoeView
                Case "Settings"
                    MainContent.Content = _viewModelLocator.SettingsView
                Case "ApiKeys"
                    MainContent.Content = _viewModelLocator.ApiKeysView
            End Select
        End Sub

    End Class

End Namespace
