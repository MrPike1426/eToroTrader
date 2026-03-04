Imports System.Windows
Imports System.Windows.Controls

Namespace TopStepTrader.UI.Controls

    ''' <summary>
    ''' A self-contained contract selector with four hardcoded micro-futures contracts.
    ''' Exposes a single string DP <c>ContractId</c> (the long-form API contract ID,
    ''' e.g. "CON.F.US.MGC.J26") for two-way binding from parent ViewModels.
    ''' </summary>
    Partial Public Class ContractSelectorControl
        Inherits UserControl

        ' ── Inner type ──────────────────────────────────────────────────────
        Public Class ContractItem
            Public Property Display As String
            Public Property Id As String
        End Class

        ' ── Hardcoded contract list ──────────────────────────────────────────
        ' NOTE: Contract months: H26 (March) expired 03/15/2026; using K26 (May) for active contracts.
        ' MGC and MCL use J26 (June) as per trading conventions.
        Private Shared ReadOnly _contracts As New List(Of ContractItem) From {
            New ContractItem With {.Display = "Please Choose", .Id = ""},
            New ContractItem With {.Display = "MGC: Micro Gold", .Id = "CON.F.US.MGC.J26"},
            New ContractItem With {.Display = "MNQ: Micro NASDAQ", .Id = "CON.F.US.MNQ.K26"},
            New ContractItem With {.Display = "MCL: Micro Oil", .Id = "CON.F.US.MCL.J26"},
            New ContractItem With {.Display = "MES: Micro S&P", .Id = "CON.F.US.MES.K26"}
        }

        ' ── Reentrancy guard ────────────────────────────────────────────────
        Private _isUpdating As Boolean = False

        ' ── Constructor ─────────────────────────────────────────────────────
        Public Sub New()
            InitializeComponent()
            ContractComboBox.ItemsSource = _contracts
            ContractComboBox.SelectedIndex = 0   ' default: "Please Choose"
        End Sub

        ' ── ContractId DP ───────────────────────────────────────────────────
        ''' <summary>
        ''' Two-way bindable long-form contract ID (e.g. "CON.F.US.MGC.J26").
        ''' Empty string means no contract selected (placeholder visible).
        ''' </summary>
        Public Property ContractId As String
            Get
                Return CStr(GetValue(ContractIdProperty))
            End Get
            Set(value As String)
                SetValue(ContractIdProperty, value)
            End Set
        End Property

        Public Shared ReadOnly ContractIdProperty As DependencyProperty =
            DependencyProperty.Register(
                NameOf(ContractId),
                GetType(String),
                GetType(ContractSelectorControl),
                New PropertyMetadata(String.Empty, AddressOf OnContractIdChanged))

        ''' <summary>
        ''' Called when the ViewModel pushes a new ContractId value in.
        ''' Finds the matching item and selects it (or resets to placeholder).
        ''' </summary>
        Private Shared Sub OnContractIdChanged(d As DependencyObject, e As DependencyPropertyChangedEventArgs)
            Dim ctl = TryCast(d, ContractSelectorControl)
            If ctl Is Nothing OrElse ctl._isUpdating Then Return

            ctl._isUpdating = True
            Try
                Dim idVal = TryCast(e.NewValue, String)
                Dim match = _contracts.FirstOrDefault(
                    Function(c) String.Equals(c.Id, idVal, StringComparison.OrdinalIgnoreCase))

                If match IsNot Nothing Then
                    ctl.ContractComboBox.SelectedItem = match
                Else
                    ctl.ContractComboBox.SelectedIndex = 0   ' reset to placeholder
                End If
            Finally
                ctl._isUpdating = False
            End Try
        End Sub

        ' ── SelectionChanged handler ─────────────────────────────────────────
        ''' <summary>
        ''' Called when the user picks a contract. Writes the long-form ID back to
        ''' the ContractId DP so the bound ViewModel property is updated.
        ''' </summary>
        Private Sub OnSelectionChanged(sender As Object, e As SelectionChangedEventArgs)
            If _isUpdating Then Return

            _isUpdating = True
            Try
                Dim item = TryCast(ContractComboBox.SelectedItem, ContractItem)
                ContractId = If(item IsNot Nothing, item.Id, String.Empty)
            Finally
                _isUpdating = False
            End Try
        End Sub

    End Class

End Namespace
