Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Settings

Namespace TopStepTrader.API

    ''' <summary>
    ''' Provides eToro API credentials for injection into every request header.
    ''' eToro authenticates via static header keys — there is no login endpoint,
    ''' no JWT token, and no refresh cycle required.
    '''   x-api-key  = Public API Key (from eToro developer portal)
    '''   x-user-key = User Key       (stored in DAMO_DEMO.txt for the demo account)
    ''' </summary>
    Public Class EToroCredentialsProvider

        Private ReadOnly _settings As ApiSettings
        Private ReadOnly _logger As ILogger(Of EToroCredentialsProvider)

        Public Sub New(options As IOptions(Of ApiSettings),
                       logger As ILogger(Of EToroCredentialsProvider))
            _settings = options.Value
            _logger = logger
        End Sub

        Public ReadOnly Property ApiKey As String
            Get
                Return _settings.ApiKey
            End Get
        End Property

        Public ReadOnly Property UserKey As String
            Get
                Return _settings.UserKey
            End Get
        End Property

        Public ReadOnly Property IsConfigured As Boolean
            Get
                Return Not String.IsNullOrEmpty(_settings.ApiKey) AndAlso
                       Not String.IsNullOrEmpty(_settings.UserKey)
            End Get
        End Property

        Public Sub AssertConfigured()
            If Not IsConfigured Then
                _logger.LogError("eToro credentials not configured. Set Api:ApiKey and Api:UserKey in appsettings.")
                Throw New InvalidOperationException(
                    "eToro API credentials are missing. Configure Api:ApiKey and Api:UserKey.")
            End If
        End Sub

    End Class

End Namespace
