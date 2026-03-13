Namespace TopStepTrader.Core.Trading

    ''' <summary>
    ''' Immutable snapshot of the current Turtle bracket state for one open position.
    ''' Levels are tracked in dollar P&amp;L terms relative to entry, then converted to
    ''' absolute prices on demand via <see cref="SlPrice"/> and <see cref="TpPrice"/>.
    '''
    ''' Turtle bracket logic:
    '''   N  = ATR(price) × DollarPerPoint          (volatility unit, fixed at entry)
    '''   Step = 0.5 × N                             (advance increment)
    '''
    '''   Bracket 0  SL = −InitialSlDollars   TP = +InitialTpDollars
    '''   On advance: NewSL = CurrentTP        NewTP = CurrentTP + Step
    '''   SL only ever moves in the favourable direction (never retreats).
    ''' </summary>
    Public NotInheritable Class TurtleBracketState

        ''' <summary>Absolute entry price at which the position was opened.</summary>
        Public ReadOnly Property EntryPrice As Decimal

        ''' <summary>"BUY" or "SELL".</summary>
        Public ReadOnly Property Side As String

        ''' <summary>
        ''' Dollar P&amp;L per one unit of price movement for this position.
        ''' eToro:    Units = EntryAmount × Leverage / EntryPrice
        ''' TopStep:  PointValue × NumberOfContracts
        ''' </summary>
        Public ReadOnly Property DollarPerPoint As Decimal

        ''' <summary>ATR volatility in dollar terms, fixed at entry. N = ATR(price) × DollarPerPoint.</summary>
        Public ReadOnly Property N As Decimal

        ''' <summary>Bracket advance increment = 0.5 × N.</summary>
        Public ReadOnly Property StepSize As Decimal

        ''' <summary>
        ''' Current SL level expressed as dollar P&amp;L relative to entry.
        ''' Negative = loss side (e.g. −10 means $10 loss triggers the stop).
        ''' </summary>
        Public ReadOnly Property CurrentSlDollars As Decimal

        ''' <summary>
        ''' Current TP level expressed as dollar P&amp;L relative to entry.
        ''' Positive = profit side (e.g. +20 means $20 gain triggers the advance).
        ''' </summary>
        Public ReadOnly Property CurrentTpDollars As Decimal

        ''' <summary>How many times the bracket has advanced. 0 = initial bracket.</summary>
        Public ReadOnly Property BracketNumber As Integer

        ''' <summary>Absolute SL price to send to the broker API.</summary>
        ''' <remarks>
        ''' <para><c>CurrentSlDollars</c> is a <em>signed</em> P&amp;L value:</para>
        ''' <list type="bullet">
        '''   <item>Negative on the initial bracket (e.g. −$10 = stop-loss level).</item>
        '''   <item>Positive after a bracket advance — the SL has been stepped up to the
        '''         previous TP level (e.g. +$20 = locks in $20 of profit).</item>
        ''' </list>
        ''' <para>
        ''' Derivation for BUY:   P&amp;L = (price − entry) × DPP
        '''   ⇒  slPrice = entry + CurrentSlDollars / DPP
        '''   Initial (−$10): slPrice = entry − 67 pts  → below entry  (loss level) ✓
        '''   Advanced (+$20): slPrice = entry + 134 pts → above entry (locked profit) ✓
        ''' </para>
        ''' <para>
        ''' Derivation for SELL:  P&amp;L = (entry − price) × DPP
        '''   ⇒  slPrice = entry − CurrentSlDollars / DPP
        '''   Initial (−$10): slPrice = entry + 67 pts  → above entry (loss level) ✓
        '''   Advanced (+$20): slPrice = entry − 134 pts → below entry (locked profit) ✓
        ''' </para>
        ''' <para>
        ''' <strong>Do NOT wrap <c>CurrentSlDollars</c> in <c>Math.Abs</c></strong> — the sign
        ''' is essential.  Stripping it causes the SL to move in the wrong direction after
        ''' bracket advance (pushes the stop further away from profit instead of locking it in).
        ''' </para>
        ''' </remarks>
        Public ReadOnly Property SlPrice As Decimal
            Get
                If DollarPerPoint = 0D Then Return EntryPrice
                ' Signed delta — preserves loss/profit direction (see remarks above).
                Dim delta = Math.Round(CurrentSlDollars / DollarPerPoint, 4)
                Return If(Side = "BUY",
                          Math.Round(EntryPrice + delta, 4),
                          Math.Round(EntryPrice - delta, 4))
            End Get
        End Property

        ''' <summary>Absolute TP price to send to the broker API.</summary>
        Public ReadOnly Property TpPrice As Decimal
            Get
                If DollarPerPoint = 0D Then Return EntryPrice
                Dim delta = Math.Round(CurrentTpDollars / DollarPerPoint, 4)
                Return If(Side = "BUY",
                          Math.Round(EntryPrice + delta, 4),
                          Math.Round(EntryPrice - delta, 4))
            End Get
        End Property

        Public Sub New(entryPrice As Decimal,
                       side As String,
                       dollarPerPoint As Decimal,
                       n As Decimal,
                       stepSize As Decimal,
                       currentSlDollars As Decimal,
                       currentTpDollars As Decimal,
                       bracketNumber As Integer)
            Me.EntryPrice = entryPrice
            Me.Side = side
            Me.DollarPerPoint = dollarPerPoint
            Me.N = n
            Me.StepSize = stepSize
            Me.CurrentSlDollars = currentSlDollars
            Me.CurrentTpDollars = currentTpDollars
            Me.BracketNumber = bracketNumber
        End Sub

    End Class

    ''' <summary>
    ''' Pure-logic, stateless Turtle bracket management.
    ''' No I/O — all methods are Shared. Engines hold a <see cref="TurtleBracketState"/>
    ''' instance and call these helpers to determine when and how to advance.
    '''
    ''' Bracket advance rule (mirrors Turtle 0.5 N step-up):
    '''   When live P&amp;L ≥ CurrentTpDollars → advance:
    '''     NewSL  = CurrentTP  (lock in the profit level)
    '''     NewTP  = CurrentTP + StepSize
    '''   SL never retreats.
    ''' </summary>
    Public NotInheritable Class TurtleBracketManager

        Private Sub New()
        End Sub

        ' ── Initialisation ──────────────────────────────────────────────────────

        ''' <summary>
        ''' Creates a new bracket state at position open time.
        ''' </summary>
        ''' <param name="entryPrice">Fill price of the entry order.</param>
        ''' <param name="side">"BUY" or "SELL".</param>
        ''' <param name="dollarPerPoint">
        '''   eToro:    EntryAmount × Leverage / EntryPrice
        '''   TopStep:  PointValue × NumberOfContracts
        ''' </param>
        ''' <param name="atrPrice">ATR value in price units (0 = unavailable).</param>
        ''' <param name="initialSlDollars">User-configured initial SL in dollars (positive value, e.g. 10).</param>
        ''' <param name="initialTpDollars">User-configured initial TP in dollars (positive value, e.g. 20).</param>
        Public Shared Function Initialise(entryPrice As Decimal,
                                          side As String,
                                          dollarPerPoint As Decimal,
                                          atrPrice As Decimal,
                                          initialSlDollars As Decimal,
                                          initialTpDollars As Decimal) As TurtleBracketState
            ' N = ATR in dollar terms
            Dim n As Decimal = 0D
            If atrPrice > 0D AndAlso dollarPerPoint > 0D Then
                n = Math.Round(atrPrice * dollarPerPoint, 4)
            End If

            ' Step = 0.5 × N. Fallback: if ATR unavailable, step = initialTpDollars / 2.
            Dim stepSize As Decimal = If(n > 0D,
                                         Math.Round(n * 0.5D, 4),
                                         Math.Round(initialTpDollars / 2D, 4))

            ' Guard: step must be at least $1 to prevent infinite loops
            If stepSize < 1D Then stepSize = 1D

            ' Initial SL is negative (loss side), TP is positive (profit side)
            Return New TurtleBracketState(
                entryPrice:=entryPrice,
                side:=side.ToUpperInvariant(),
                dollarPerPoint:=dollarPerPoint,
                n:=n,
                stepSize:=stepSize,
                currentSlDollars:=-Math.Abs(initialSlDollars),
                currentTpDollars:=Math.Abs(initialTpDollars),
                bracketNumber:=0)
        End Function

        ' ── Advance logic ────────────────────────────────────────────────────────

        ''' <summary>
        ''' Rescales the bracket's dollar-denominated fields to reflect a new total
        ''' DollarPerPoint after an additional position has been added (scale-in).
        '''
        ''' Because DollarPerPoint = total units open, every dollar threshold (N, StepSize,
        ''' CurrentSLDollars, CurrentTPDollars) must scale proportionally so the SAME
        ''' price move produces the correct aggregate P&amp;L in dollar terms.
        ''' The absolute SL/TP PRICES are unchanged — only the dollar thresholds scale.
        '''
        ''' Example: initial DPP=0.15 (1 position), after 3 scale-ins DPP=0.60 (4 positions).
        '''   The bracket's CurrentTPDollars must be 4× the initial value so the advance
        '''   triggers at the same price level ($20 initial TP → $80 total-portfolio TP).
        ''' </summary>
        Public Shared Function Rescale(state As TurtleBracketState,
                                       newDollarPerPoint As Decimal) As TurtleBracketState
            If state Is Nothing OrElse state.DollarPerPoint = 0D OrElse newDollarPerPoint <= 0D Then
                Return state
            End If
            Dim f = newDollarPerPoint / state.DollarPerPoint  ' scale factor
            Return New TurtleBracketState(
                entryPrice:=state.EntryPrice,
                side:=state.Side,
                dollarPerPoint:=newDollarPerPoint,
                n:=Math.Round(state.N * f, 4),
                stepSize:=Math.Round(state.StepSize * f, 4),
                currentSlDollars:=Math.Round(state.CurrentSlDollars * f, 4),
                currentTpDollars:=Math.Round(state.CurrentTpDollars * f, 4),
                bracketNumber:=state.BracketNumber)
        End Function

        ''' <summary>
        ''' Returns True when the live position P&amp;L has reached or exceeded the current
        ''' TP level and the bracket should advance.
        ''' </summary>
        ''' <param name="state">Current bracket state.</param>
        ''' <param name="currentPnlDollars">
        '''   Live unrealised P&amp;L in dollars (positive = profit, negative = loss).
        '''   eToro:    (currentPrice − entryPrice) × dollarPerPoint  (for BUY)
        '''   TopStep:  same formula using PointValue × contracts
        ''' </param>
        Public Shared Function ShouldAdvance(state As TurtleBracketState,
                                             currentPnlDollars As Decimal) As Boolean
            If state Is Nothing Then Return False
            Return currentPnlDollars >= state.CurrentTpDollars
        End Function

        ''' <summary>
        ''' Returns True when the live position P&amp;L has hit or breached the current SL level.
        ''' Used by the backtest engine for intrabar SL detection.
        ''' </summary>
        Public Shared Function IsSlBreached(state As TurtleBracketState,
                                            currentPnlDollars As Decimal) As Boolean
            If state Is Nothing Then Return False
            Return currentPnlDollars <= state.CurrentSlDollars
        End Function

        ''' <summary>
        ''' Advances the bracket by one step:
        '''   NewSL = CurrentTP  (locks in the previous TP level as the new floor)
        '''   NewTP = CurrentTP + StepSize
        ''' Returns a new immutable <see cref="TurtleBracketState"/>. The original is unchanged.
        ''' </summary>
        Public Shared Function Advance(state As TurtleBracketState) As TurtleBracketState
            Dim newSlDollars = state.CurrentTpDollars          ' SL steps up to where TP was
            Dim newTpDollars = state.CurrentTpDollars + state.StepSize

            Return New TurtleBracketState(
                entryPrice:=state.EntryPrice,
                side:=state.Side,
                dollarPerPoint:=state.DollarPerPoint,
                n:=state.N,
                stepSize:=state.StepSize,
                currentSlDollars:=newSlDollars,
                currentTpDollars:=newTpDollars,
                bracketNumber:=state.BracketNumber + 1)
        End Function

        ' ── Unit conversion helpers ──────────────────────────────────────────────

        ''' <summary>Converts a dollar P&amp;L amount to a price delta (price units moved).</summary>
        Public Shared Function DollarsToPriceDelta(dollars As Decimal,
                                                    dollarPerPoint As Decimal) As Decimal
            If dollarPerPoint = 0D Then Return 0D
            Return Math.Round(Math.Abs(dollars) / dollarPerPoint, 4)
        End Function

        ''' <summary>Converts a price delta to a dollar P&amp;L amount.</summary>
        Public Shared Function PriceDeltaToDollars(priceDelta As Decimal,
                                                    dollarPerPoint As Decimal) As Decimal
            Return Math.Round(Math.Abs(priceDelta) * dollarPerPoint, 4)
        End Function

        ''' <summary>
        ''' Converts a dollar SL or TP amount to the equivalent number of ticks.
        ''' Used by TopStep engines which submit tick-based bracket orders.
        ''' </summary>
        ''' <param name="dollars">Dollar amount (positive).</param>
        ''' <param name="tickSize">Price units per tick (e.g. 0.25 for MES/MNQ).</param>
        ''' <param name="pointValue">Dollar value per full point (e.g. $5 for MES, $2 for MNQ).</param>
        ''' <param name="contracts">Number of contracts open.</param>
        Public Shared Function DollarsToTicks(dollars As Decimal,
                                               tickSize As Decimal,
                                               pointValue As Decimal,
                                               contracts As Integer) As Integer
            If tickSize = 0D OrElse pointValue = 0D OrElse contracts = 0 Then Return 0
            Dim dollarPerTick = tickSize * pointValue * contracts
            Return CInt(Math.Round(Math.Abs(dollars) / dollarPerTick, MidpointRounding.AwayFromZero))
        End Function

        ''' <summary>
        ''' Computes the live unrealised P&amp;L in dollars from current price and entry context.
        ''' </summary>
        Public Shared Function ComputePnlDollars(state As TurtleBracketState,
                                                  currentPrice As Decimal) As Decimal
            If state Is Nothing OrElse state.DollarPerPoint = 0D Then Return 0D
            Dim priceDiff = If(state.Side = "BUY",
                               currentPrice - state.EntryPrice,
                               state.EntryPrice - currentPrice)
            Return Math.Round(priceDiff * state.DollarPerPoint, 4)
        End Function

    End Class

End Namespace
