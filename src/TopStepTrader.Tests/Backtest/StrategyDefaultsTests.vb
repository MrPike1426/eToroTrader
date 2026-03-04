Imports TopStepTrader.Core.Trading
Imports Xunit

Namespace TopStepTrader.Tests.Backtest

    ''' <summary>
    ''' Unit tests for StrategyDefaults — parameter lookup logic with no external dependencies.
    ''' TICKET-006 Phase 5.
    ''' </summary>
    Public Class StrategyDefaultsTests

        ' ══════════════════════════════════════════════════════════════════
        ' TryGet — known strategies
        ' ══════════════════════════════════════════════════════════════════

        <Fact>
        Public Sub TryGet_EmaRsiCombined_ReturnsCorrectDefaults()
            Dim result = StrategyDefaults.TryGet("EMA/RSI Combined")

            Assert.NotNull(result)
            Assert.Equal("50000", result.Capital)
            Assert.Equal("4", result.Qty)
            Assert.Equal("20", result.TakeProfitTicks)
            Assert.Equal("10", result.StopLossTicks)
        End Sub

        ' ══════════════════════════════════════════════════════════════════
        ' TryGet — missing / invalid input
        ' ══════════════════════════════════════════════════════════════════

        <Fact>
        Public Sub TryGet_UnknownStrategy_ReturnsNothing()
            Dim result = StrategyDefaults.TryGet("Nonexistent Strategy")

            Assert.Null(result)
        End Sub

        <Fact>
        Public Sub TryGet_EmptyString_ReturnsNothing()
            Dim result = StrategyDefaults.TryGet("")

            Assert.Null(result)
        End Sub

        <Fact>
        Public Sub TryGet_NullString_ReturnsNothing()
            Dim result = StrategyDefaults.TryGet(Nothing)

            Assert.Null(result)
        End Sub

        <Fact>
        Public Sub TryGet_WhitespaceOnly_ReturnsNothing()
            ' Whitespace is not a valid strategy name but IsNullOrEmpty won't catch it.
            ' Confirm the dictionary simply doesn't find it — returns Nothing gracefully.
            Dim result = StrategyDefaults.TryGet("   ")

            Assert.Null(result)
        End Sub

        ' ══════════════════════════════════════════════════════════════════
        ' TryGet — case insensitivity
        ' ══════════════════════════════════════════════════════════════════

        <Fact>
        Public Sub TryGet_LowerCase_ReturnsSameDefaults()
            ' Dictionary uses StringComparer.OrdinalIgnoreCase
            Dim result = StrategyDefaults.TryGet("ema/rsi combined")

            Assert.NotNull(result)
            Assert.Equal("50000", result.Capital)
        End Sub

        <Fact>
        Public Sub TryGet_UpperCase_ReturnsSameDefaults()
            Dim result = StrategyDefaults.TryGet("EMA/RSI COMBINED")

            Assert.NotNull(result)
            Assert.Equal("4", result.Qty)
        End Sub

        ' ══════════════════════════════════════════════════════════════════
        ' Defaults collection — design rules
        ' ══════════════════════════════════════════════════════════════════

        <Fact>
        Public Sub Defaults_ContainsOnlyEmaRsiCombined()
            ' TICKET-006 design decision: only one combined strategy registered.
            Assert.Single(StrategyDefaults.Defaults)
            Assert.True(StrategyDefaults.Defaults.ContainsKey("EMA/RSI Combined"))
        End Sub

        <Fact>
        Public Sub Defaults_DoesNotContainSingleIndicatorStrategies()
            ' Single-indicator strategies are explicitly excluded by design.
            Assert.False(StrategyDefaults.Defaults.ContainsKey("RSI Reversal"))
            Assert.False(StrategyDefaults.Defaults.ContainsKey("Double Bottom"))
            Assert.False(StrategyDefaults.Defaults.ContainsKey("EMA Crossover"))
        End Sub

    End Class

End Namespace
