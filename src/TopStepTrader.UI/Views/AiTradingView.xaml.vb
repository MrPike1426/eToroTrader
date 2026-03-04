Imports System.Collections.Specialized
Imports System.Windows.Controls
Imports TopStepTrader.UI.ViewModels

Namespace TopStepTrader.UI.Views

    ''' <summary>
    ''' Code-behind for the AI-Assisted Trading tab.
    ''' Loads data via the ViewModel and auto-scrolls the execution log.
    ''' </summary>
    Partial Public Class AiTradingView
        Inherits UserControl

        Private ReadOnly _vm As AiTradingViewModel

        Public Sub New(viewModel As AiTradingViewModel)
            InitializeComponent()
            _vm = viewModel
            DataContext = _vm

            ' Wire log auto-scroll
            AddHandler _vm.LogEntries.CollectionChanged, AddressOf OnLogChanged

            ' Load accounts + contracts in the background
            _vm.LoadDataAsync()
        End Sub

        ''' <summary>
        ''' Scroll the log ListBox to the bottom whenever a new entry is added.
        ''' </summary>
        Private Sub OnLogChanged(sender As Object, e As NotifyCollectionChangedEventArgs)
            If e.Action = NotifyCollectionChangedAction.Add AndAlso LogList.Items.Count > 0 Then
                LogList.ScrollIntoView(LogList.Items(LogList.Items.Count - 1))
            End If
        End Sub

    End Class

End Namespace
