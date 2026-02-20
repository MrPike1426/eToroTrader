Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Requests

    Public Class PlaceOrderRequest
        <JsonPropertyName("accountId")>
        Public Property AccountId As Long

        ''' <summary>Contract ID as string e.g. "CON.F.US.EP.H26"</summary>
        <JsonPropertyName("contractId")>
        Public Property ContractId As String = String.Empty

        ''' <summary>1=Limit, 2=Market, 3=Stop, 4=StopLimit</summary>
        <JsonPropertyName("type")>
        Public Property OrderType As Integer

        ''' <summary>0=Buy, 1=Sell (Ask)</summary>
        <JsonPropertyName("side")>
        Public Property Side As Integer

        <JsonPropertyName("size")>
        Public Property Size As Integer = 1

        <JsonPropertyName("limitPrice")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property LimitPrice As Double?

        <JsonPropertyName("stopPrice")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property StopPrice As Double?
    End Class

End Namespace
