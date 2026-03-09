Namespace TopStepTrader.Core.Settings

    ''' <summary>
    ''' API key storage model.  4 named provider slots + 4 user-labelled future slots.
    ''' Serialised to %LOCALAPPDATA%\TopStepTrader\apikeys.json by ApiKeyStore.
    ''' </summary>
    Public Class ApiKeySettings
        ' ── Named providers — credential (non-secret) + API key (secret) ────────────
        Public Property EtoroKeyName As String = String.Empty       ' "Key name" shown in eToro portal
        Public Property EtoroApiKey As String = String.Empty
        Public Property TopStepXUsername As String = String.Empty   ' Account email address
        Public Property TopStepXApiKey As String = String.Empty
        Public Property ClaudeOrgId As String = String.Empty        ' Organisation / Workspace ID (optional)
        Public Property ClaudeApiKey As String = String.Empty
        Public Property BinanceApiKey As String = String.Empty      ' API Key (public half)
        Public Property BinanceSecretKey As String = String.Empty   ' Secret Key (private half)

        ' ── Future slots — editable label + username/email + API key ──────────────
        Public Property Future1Label As String = String.Empty
        Public Property Future1Username As String = String.Empty
        Public Property Future1Key As String = String.Empty
        Public Property Future2Label As String = String.Empty
        Public Property Future2Username As String = String.Empty
        Public Property Future2Key As String = String.Empty
        Public Property Future3Label As String = String.Empty
        Public Property Future3Username As String = String.Empty
        Public Property Future3Key As String = String.Empty
        Public Property Future4Label As String = String.Empty
        Public Property Future4Username As String = String.Empty
        Public Property Future4Key As String = String.Empty
    End Class

End Namespace
