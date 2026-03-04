Imports Microsoft.Extensions.Logging

Namespace TopStepTrader.API.Http

    ''' <summary>
    ''' eToro has no login endpoint — authentication is handled by static header keys.
    ''' This client validates that credentials are configured and is otherwise a no-op.
    ''' </summary>
    Public Class AuthClient

        Private ReadOnly _credentials As EToroCredentialsProvider
        Private ReadOnly _logger As ILogger(Of AuthClient)

        Public Sub New(credentials As EToroCredentialsProvider,
                       logger As ILogger(Of AuthClient))
            _credentials = credentials
            _logger = logger
        End Sub

        ''' <summary>
        ''' Validates that eToro API credentials are present in configuration.
        ''' Returns True if both ApiKey and UserKey are set; False otherwise.
        ''' </summary>
        Public Function ValidateCredentialsAsync() As Task(Of Boolean)
            If _credentials.IsConfigured Then
                _logger.LogInformation("eToro credentials present — no login required.")
                Return Task.FromResult(True)
            End If
            _logger.LogError("eToro credentials missing. Set Api:ApiKey and Api:UserKey.")
            Return Task.FromResult(False)
        End Function

    End Class

End Namespace
