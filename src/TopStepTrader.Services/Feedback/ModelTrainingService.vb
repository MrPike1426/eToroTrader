Imports System.Threading
Imports Microsoft.Extensions.Logging
Imports Microsoft.Extensions.Options
Imports TopStepTrader.Core.Enums
Imports TopStepTrader.Core.Interfaces
Imports TopStepTrader.Core.Models
Imports TopStepTrader.Core.Settings
Imports TopStepTrader.Data.Repositories
Imports TopStepTrader.ML.Features
Imports TopStepTrader.ML.Prediction
Imports TopStepTrader.ML.Training

Namespace TopStepTrader.Services.Feedback

    ''' <summary>
    ''' Implements the ML feedback loop retraining pipeline:
    '''   1. Load resolved trade outcomes from the DB.
    '''   2. Fetch bar history around each outcome to re-extract feature vectors.
    '''   3. Train a new model using real outcome labels (IsWinner) blended
    '''      with historical look-ahead labels from bar data.
    '''   4. Save the new model; ModelManager hot-reloads it automatically.
    ''' </summary>
    Public Class ModelTrainingService
        Implements IModelTrainingService

        Private ReadOnly _outcomeRepo As TradeOutcomeRepository
        Private ReadOnly _barRepo As BarRepository
        Private ReadOnly _trainer As SignalModelTrainer
        Private ReadOnly _featureExtractor As BarFeatureExtractor
        Private ReadOnly _modelManager As ModelManager
        Private ReadOnly _mlSettings As MLSettings
        Private ReadOnly _tradingSettings As TradingSettings
        Private ReadOnly _logger As ILogger(Of ModelTrainingService)

        Public Event ModelRetrained As EventHandler(Of ModelRetrainedEventArgs) _
            Implements IModelTrainingService.ModelRetrained

        Public Sub New(outcomeRepo As TradeOutcomeRepository,
                       barRepo As BarRepository,
                       trainer As SignalModelTrainer,
                       featureExtractor As BarFeatureExtractor,
                       modelManager As ModelManager,
                       mlOptions As IOptions(Of MLSettings),
                       tradingOptions As IOptions(Of TradingSettings),
                       logger As ILogger(Of ModelTrainingService))
            _outcomeRepo = outcomeRepo
            _barRepo = barRepo
            _trainer = trainer
            _featureExtractor = featureExtractor
            _modelManager = modelManager
            _mlSettings = mlOptions.Value
            _tradingSettings = tradingOptions.Value
            _logger = logger
        End Sub

        ' ── IModelTrainingService ─────────────────────────────────────────────

        Public Async Function RetrainAsync(cancel As CancellationToken,
                                           Optional contractId As String = Nothing) _
            As Task(Of ModelMetrics) Implements IModelTrainingService.RetrainAsync

            _logger.LogInformation("Starting model retraining with feedback data...")

            ' 1. Get resolved outcomes from the last 6 months
            Dim from = DateTimeOffset.UtcNow.AddMonths(-6)
            Dim outcomes = Await _outcomeRepo.GetResolvedOutcomesAsync(from)
            _logger.LogInformation("Found {Count} resolved outcomes for retraining", outcomes.Count)

            ' 2. Get historical bar data (last 3 months) for base training.
            '    UAT-BUG-004: When an explicit contractId is supplied (backtest context) use only
            '    that contract's bars.  Otherwise fall back to TradingSettings.ActiveContractIds
            '    (live-trading default).  Avoids the mismatch where the backtest page downloads
            '    bars for the user-selected contract but training queried a different config list.
            Dim barFrom = DateTimeOffset.UtcNow.AddMonths(-3)
            Dim tf = CType(_tradingSettings.DefaultTimeframe, BarTimeframe)

            Dim contractIds As IEnumerable(Of String)
            If Not String.IsNullOrWhiteSpace(contractId) Then
                contractIds = {contractId}
                _logger.LogInformation("RetrainAsync: using explicit contractId={ContractId}", contractId)
            Else
                contractIds = _tradingSettings.ActiveContractIds
                _logger.LogInformation("RetrainAsync: using ActiveContractIds ({Count} entries)",
                                       _tradingSettings.ActiveContractIds.Count)
            End If

            Dim allBars As New List(Of MarketBar)()
            For Each cid In contractIds
                If cancel.IsCancellationRequested Then Throw New OperationCanceledException(cancel)
                Dim bars = Await _barRepo.GetBarsAsync(cid, tf, barFrom, DateTimeOffset.UtcNow, cancel)
                allBars.AddRange(bars)
            Next

            If allBars.Count < 200 Then
                _logger.LogWarning("Insufficient bar history ({Count} bars) for retraining. Skipping.", allBars.Count)
                Return Nothing
            End If

            ' 3. Build override labels from real outcomes
            '    Map each outcome's entryTime to the nearest bar timestamp
            Dim outcomeLabels As New Dictionary(Of DateTimeOffset, Boolean)()
            For Each o In outcomes
                If o.IsWinner.HasValue Then
                    outcomeLabels(o.EntryTime) = o.IsWinner.Value
                End If
            Next

            ' 4. Train — pass the real outcome overrides to the trainer
            Dim outputPath = _modelManager.GetNextModelPath()
            Dim metrics = _trainer.TrainAndSave(allBars, outputPath, outcomeLabels:=outcomeLabels)

            _logger.LogInformation("Retraining complete — saved to {Path}", outputPath)
            _logger.LogInformation("New model metrics: Accuracy={Acc:P1}, AUC={AUC:F3}, F1={F1:F3}",
                                   metrics.Accuracy, metrics.AUC, metrics.F1Score)

            ' ModelManager's FileSystemWatcher picks up the new file and hot-reloads automatically.
            RaiseEvent ModelRetrained(Me, New ModelRetrainedEventArgs(metrics))
            Return metrics
        End Function

        Public Async Function GetRollingWinRateAsync(Optional windowSize As Integer = 50) _
            As Task(Of Single) Implements IModelTrainingService.GetRollingWinRateAsync
            Return Await _outcomeRepo.GetRollingWinRateAsync(windowSize)
        End Function

        Public Async Function GetOutcomeCountAsync() _
            As Task(Of Integer) Implements IModelTrainingService.GetOutcomeCountAsync
            Return Await _outcomeRepo.GetCountAsync()
        End Function

    End Class

End Namespace
