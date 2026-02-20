Imports System.Text.Json.Serialization

Namespace TopStepTrader.API.Models.Requests

    Public Class RetrieveBarsRequest
        <JsonPropertyName("contractId")>
        Public Property ContractId As Integer

        ''' <summary>1=1min, 2=5min, 3=15min, 4=30min, 5=1hr, 6=1day</summary>
        <JsonPropertyName("unit")>
        Public Property Unit As Integer = 2

        <JsonPropertyName("unitsBack")>
        Public Property UnitsBack As Integer = 500

        <JsonPropertyName("startTime")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property StartTime As String

        <JsonPropertyName("endTime")>
        <JsonIgnore(Condition:=JsonIgnoreCondition.WhenWritingNull)>
        Public Property EndTime As String

        <JsonPropertyName("includeMidnight")>
        Public Property IncludeMidnight As Boolean = True
    End Class

End Namespace
