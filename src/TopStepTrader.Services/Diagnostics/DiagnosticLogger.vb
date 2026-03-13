Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports Microsoft.Extensions.Logging
Imports TopStepTrader.Core.Models.Diagnostics

Namespace TopStepTrader.Services.Diagnostics

    ''' <summary>
    ''' Writes high-fidelity trading diagnostic entries to a JSONL (JSON Lines) file
    ''' during an 8-hour test session.
    '''
    ''' File format: one JSON object per line.  Lines starting with '#' are comments.
    ''' Filename pattern: diag_YYYY-MM-DD_HH-mm-ss_{CONTRACT}_{SESSION}.jsonl
    ''' Output folder:    [My Documents]\eToroTrader_Diagnostics\
    '''
    ''' Thread-safe: all writes are serialised via SyncLock.
    ''' AutoFlush enabled so no data is lost if the process crashes.
    '''
    ''' ── Analysis workflow ───────────────────────────────────────────────────
    ''' Python / pandas:
    '''   import pandas as pd
    '''   df = pd.read_json("diag_*.jsonl", lines=True, comment="#")
    '''   placed = df[df.EventType == "SIGNAL_PLACED"]
    '''   pm     = df[df.EventType == "POSTMORTEM"]
    '''   merged = placed.merge(pm, on="TradeId", suffixes=("_entry","_close"))
    '''
    ''' Excel Power Query:
    '''   1. Data → Get Data → From File → From JSON
    '''   2. Filter EventType column
    ''' ────────────────────────────────────────────────────────────────────────
    ''' </summary>
    Public Class DiagnosticLogger
        Implements IDisposable

        ' ── Dependencies ──────────────────────────────────────────────────────
        Private ReadOnly _logger As ILogger(Of DiagnosticLogger)

        ' ── Session state ─────────────────────────────────────────────────────
        Private _writer As StreamWriter = Nothing
        Private ReadOnly _lock As New Object()
        Private _filePath As String = String.Empty
        Private _sessionId As String = String.Empty
        Private _entryCount As Integer = 0
        Private _sessionStartUtc As DateTimeOffset = DateTimeOffset.MinValue
        Private _disposed As Boolean = False

        ' ── JSON serialiser options ────────────────────────────────────────────
        ' WriteIndented=False for compact JSONL.
        ' SnakeCaseLower converts PascalCase property names to snake_case automatically
        '   (e.g. BbUpper → bb_upper, MaxFavorableExcursion → max_favorable_excursion).
        ' WhenWritingNull omits null nested sections (Settings/Outcome) from NO_SIGNAL
        '   entries to keep file size manageable.
        Private Shared ReadOnly _jsonOpts As New JsonSerializerOptions With {
            .WriteIndented = False,
            .DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            .PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }

        ' ── Public surface ────────────────────────────────────────────────────

        ''' <summary>Full path of the current JSONL log file, or empty when no session is active.</summary>
        Public ReadOnly Property FilePath As String
            Get
                Return _filePath
            End Get
        End Property

        ''' <summary>8-character uppercase session ID embedded in the filename.</summary>
        Public ReadOnly Property SessionId As String
            Get
                Return _sessionId
            End Get
        End Property

        ''' <summary>True when a session file is open and accepting writes.</summary>
        Public ReadOnly Property IsActive As Boolean
            Get
                Return _writer IsNot Nothing
            End Get
        End Property

        ''' <summary>Total entries written this session.</summary>
        Public ReadOnly Property EntryCount As Integer
            Get
                Return _entryCount
            End Get
        End Property

        Public Sub New(logger As ILogger(Of DiagnosticLogger))
            _logger = logger
        End Sub

        ' ── Session management ────────────────────────────────────────────────

        ''' <summary>
        ''' Opens a new JSONL log file for the 8-hour diagnostic session.
        ''' Safe to call multiple times — closes any existing session first.
        ''' </summary>
        Public Sub StartSession(contractId As String, strategyName As String)
            SyncLock _lock
                CloseWriterInternal()

                _sessionId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()
                _entryCount = 0
                _sessionStartUtc = DateTimeOffset.UtcNow

                Dim ts = _sessionStartUtc.ToString("yyyy-MM-dd_HH-mm-ss",
                                                   System.Globalization.CultureInfo.InvariantCulture)

                ' Sanitise contractId for use in a filename
                Dim invalidChars = Path.GetInvalidFileNameChars()
                Dim safeContract = String.Join("-", contractId.Split(invalidChars))

                Dim dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "eToroTrader_Diagnostics")

                Directory.CreateDirectory(dir)

                _filePath = Path.Combine(dir, $"diag_{ts}_{safeContract}_{_sessionId}.jsonl")

                _writer = New StreamWriter(_filePath,
                                           append:=False,
                                           encoding:=Encoding.UTF8) With {.AutoFlush = True}

                ' Human-readable header comments (stripped by JSONL parsers using comment="#")
                _writer.WriteLine($"# eToroTrader Diagnostic Log")
                _writer.WriteLine($"# Session   : {_sessionId}")
                _writer.WriteLine($"# Contract  : {contractId}")
                _writer.WriteLine($"# Strategy  : {strategyName}")
                _writer.WriteLine($"# Started   : {_sessionStartUtc:o}")
                _writer.WriteLine($"# Format    : JSONL — one JSON object per line; skip lines starting with '#'")
                _writer.WriteLine($"# EventTypes: TRADE | REJECT | NO_SIGNAL")
                _writer.WriteLine($"# Key fields: trade_id (TRADE records written complete on close with outcome)")
                _writer.WriteLine($"#             noise_check.is_sl_inside_noise=true → SL inside bar noise (Bad Settings)")
                _writer.WriteLine($"#             noise_check.effective_slippage_ratio → Spread/SL (>1 = spread > stop!)")
                _writer.WriteLine($"# JSON policy: snake_case keys; null nested sections omitted (WhenWritingNull)")

                _logger.LogInformation(
                    "[Diagnostics] Session {Id} opened → {Path}", _sessionId, _filePath)
            End SyncLock
        End Sub

        ''' <summary>
        ''' Writes a single <see cref="DiagnosticLogEntry"/> to the JSONL file immediately.
        ''' The SessionId is stamped here so callers don't need to track it.
        ''' No-op when no session is active.
        ''' </summary>
        Public Sub WriteEntry(entry As DiagnosticLogEntry)
            If _writer Is Nothing Then Return

            SyncLock _lock
                Try
                    If _writer Is Nothing Then Return
                    entry.SessionId = _sessionId
                    Dim json = JsonSerializer.Serialize(entry, _jsonOpts)
                    _writer.WriteLine(json)
                    _entryCount += 1
                Catch ex As Exception
                    _logger.LogWarning(ex,
                        "[Diagnostics] Write failed for {Type} entry (TradeId={Id})",
                        entry.EventType, entry.TradeId)
                End Try
            End SyncLock
        End Sub

        ''' <summary>
        ''' Writes a summary footer comment and closes the log file.
        ''' Called automatically by Stop() and Dispose().
        ''' </summary>
        Public Sub CloseSession()
            SyncLock _lock
                If _writer IsNot Nothing Then
                    Try
                        Dim durationMin = (DateTimeOffset.UtcNow - _sessionStartUtc).TotalMinutes
                        _writer.WriteLine($"# Session closed: {DateTimeOffset.UtcNow:o} | Duration: {durationMin:F1} min | Total entries: {_entryCount}")
                    Catch
                    End Try
                End If
                CloseWriterInternal()
                _logger.LogInformation(
                    "[Diagnostics] Session {Id} closed — {Count} entries written to {Path}",
                    _sessionId, _entryCount, _filePath)
            End SyncLock
        End Sub

        ' ── Private helpers ───────────────────────────────────────────────────

        Private Sub CloseWriterInternal()
            Try
                _writer?.Flush()
                _writer?.Close()
                _writer?.Dispose()
            Catch
            End Try
            _writer = Nothing
        End Sub

        Public Sub Dispose() Implements IDisposable.Dispose
            If Not _disposed Then
                CloseSession()
                _disposed = True
            End If
        End Sub

    End Class

End Namespace
