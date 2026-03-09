-- Seed data: initial eToro DEMO trading platform tickets
-- Run AFTER init:  dotnet run --project DbQuery -- init
-- Then open the db and run this file, OR add a 'seed' command to the CLI.

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES

('TICKET-001', 'SIT Testing', 'Critical', 'Critical',
 'Verify eToro DEMO API connectivity end-to-end',
 'Confirm x-api-key + x-user-key authentication resolves 401. Test GET /portfolio, instrument search and candles endpoint. Verify all three eToro headers are sent correctly on every request.',
 'Damo', 5, 'api,auth,connectivity',
 'Auth headers implemented. Public key set in appsettings.json. Awaiting user runtime confirmation.'),

('TICKET-002', 'For Development', 'High', 'High',
 'Resolve real eToro instrumentIds for FavouriteContracts',
 'Call GET /api/v1/market-data/search for each symbol (GOLD, OIL, NSDQ100, SPX500) to obtain the real numeric instrumentId. Update FavouriteContracts.vb and appsettings ActiveContractIds with confirmed IDs.',
 'Damo', 3, 'api,market-data,configuration', NULL),

('TICKET-003', 'For Development', 'High', 'High',
 'Test SL/TP order placement on eToro DEMO account',
 'Place a test market order via the by-units endpoint with StopLossRate and TakeProfitRate set. Verify: orderId returned, positionId resolved, SL/TP visible on eToro portal. Test close-position endpoint.',
 'Damo', 8, 'trading,orders,sl-tp,demo', NULL),

('TICKET-004', 'For Development', 'High', 'Medium',
 'Implement ContractMetadataService — symbol to instrumentId cache',
 'Build a singleton service that caches ticker→instrumentId lookups from the eToro market-data/search endpoint. SniperExecutionEngine should call this before placing any order to populate Order.InstrumentId.',
 'Damo', 6, 'api,market-data,architecture', NULL),

('TICKET-005', 'Backlog', 'Medium', 'Medium',
 'Implement eToro WebSocket client for real-time market data',
 'Replace MarketHubClient stub with a real wss:// WebSocket connection to the eToro streaming API. Subscribe to instrument quotes and raise QuoteReceived events for MarketDataService consumers.',
 'Damo', 13, 'api,websocket,market-data,real-time',
 'Blocked by TICKET-001 (need confirmed auth before testing WebSocket)'),

('TICKET-006', 'Backlog', 'Medium', 'Medium',
 'Implement eToro WebSocket client for real-time user events',
 'Replace UserHubClient stub with a real wss:// WebSocket connection for position updates and fills. Map eToro position events to the existing OrderFillReceived / PositionUpdated event interface.',
 'Damo', 10, 'api,websocket,user-events,real-time',
 'BlockedBy TICKET-005 — implement market hub first to establish WebSocket pattern'),

('TICKET-007', 'Backlog', 'Low', 'Low',
 'Update UI instrument dropdowns to use eToro instrument names',
 'ContractSelectorControl and AI Trade tab currently show TopStep contract codes. Update to display eToro instrument names and instrumentIds once TICKET-002 confirms the real IDs.',
 'Damo', 4, 'ui,instruments',
 'BlockedBy TICKET-002'),

('TICKET-008', 'Backlog', 'Medium', 'Low',
 'House-keeping — remove dead/redundant code after UAT',
 'Remove empty stub files (LoginKeyRequest, AuthResponse, AccountSearchRequest, ContractAvailableRequest, RetrieveBarsRequest, CancelOrderRequest old shape). Remove no-op TokenRefreshWorker from DI. Delete ProjectX comments. Rename TokenManager.vb file to match EToroCredentialsProvider class name.',
 'GitHub Copilot', 10, 'maintenance,cleanup',
 'Deferred until full eToro UAT is complete. BlockedBy TICKET-001,TICKET-003');

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-009', 'Backlog', 'Medium', 'Low',
 'Extend Instrument Selector — live API search for any eToro instrument',
 'UAT-002 passed for the Golden 5 favourites only.  ContractSelectorControl already contains a skeleton live-search path (OnSearchTimerTick → ContractClient.SearchInstrumentAsync) but it has never been exercised or formally validated.

Scope:
1. Confirm that ContractClient.SearchInstrumentAsync returns usable results for partial / full symbols (e.g. ''GBPJPY'', ''Gold'', ''Apple'').  The current endpoint uses internalSymbolFull for an exact match — investigate whether the eToro search API also accepts a looser query param (e.g. internalSymbol or displayName) to support partial-entry lookup.
2. Add a visible loading indicator (e.g. greyed-out "Searching..." item in the drop-down) while the async call is in-flight.
3. Handle zero-result responses — insert a non-selectable "No results found" row instead of leaving the drop-down empty.
4. Cap results at 15 items (already coded) and validate the "Search Results" section header renders correctly beneath the Favourites group.
5. Confirm that selecting a live-search result correctly writes ContractId (symbol string) and InstrumentId (numeric eToro ID) back to the consuming ViewModel, identical to selecting a favourite.
6. Add error-state handling — on API timeout or auth failure, silently fall back to favourites-only list and log the exception through ILogger.
7. UAT: type ''GBPJPY'' in each page Instrument Selector — the matching eToro instrument should appear in the drop-down within ~500 ms of the user pausing typing.

Files expected to change:
  ContractSelectorControl.xaml.vb  — loading state, empty-result row, ILogger injection
  ContractSelectorControl.xaml     — optional loading animation / style tweak
  ContractClient.vb                — verify / adjust search query parameter for partial match

Coordination: TICKET-004 (ContractMetadataService) caches the same ticker→instrumentId data; align to avoid duplicate resolution logic.',
 'Damo', 10, 'feature,ui,api', NULL);

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-010', 'Complete', 'Critical', 'Critical',
 'BUG — Wrong DB connection string breaks bar-check and order placement',
 'Two runtime failures traced to a single root cause:

Symptoms observed:
  A) AI-Assisted Trading — "Error during bar check: Connection string keyword ''database'' is not supported"
     (StrategyExecutionEngine → BarIngestionService → AppDbContext)
  B) Test Trade — "Order error: Connection string keyword ''database'' is not supported"
     (IOrderService.PlaceOrderAsync → OrderRepository → AppDbContext)

Root cause:
  appsettings.json DefaultConnection contained a SQL Server format string:
    "Server=localhost;Database=TopStepTraderDb;Trusted_Connection=True;..."
  DataServiceExtensions.vb calls opts.UseSqlite(dbPath).  Its bare-filename
  guard checks for "Data Source" prefix — finding none, it prepended "Data Source="
  and passed Path.Combine(BaseDir, <entire SQL Server string>) to SQLite.
  SQLite''s connection-string parser then hit ";Database=" and threw.

Fix applied:
  appsettings.json + appsettings.template.json DefaultConnection changed to
  bare filename "TopStepTrader.db".  DataServiceExtensions expands this to an
  absolute path (Data Source=<exeDir>\TopStepTrader.db) at startup.',
 'Damo', 1, 'bug,database,configuration,critical', 'Fixed in appsettings.json + appsettings.template.json');

-- Dependency cross-links
UPDATE Tickets SET Blocks    = 'TICKET-005' WHERE TicketId = 'TICKET-001';
UPDATE Tickets SET BlockedBy = 'TICKET-001' WHERE TicketId = 'TICKET-005';
UPDATE Tickets SET Blocks    = 'TICKET-006' WHERE TicketId = 'TICKET-005';
UPDATE Tickets SET BlockedBy = 'TICKET-005' WHERE TicketId = 'TICKET-006';
UPDATE Tickets SET Blocks    = 'TICKET-007' WHERE TicketId = 'TICKET-002';
UPDATE Tickets SET BlockedBy = 'TICKET-002' WHERE TicketId = 'TICKET-007';
UPDATE Tickets SET BlockedBy = 'TICKET-001,TICKET-003' WHERE TicketId = 'TICKET-008';

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-011', 'Complete', 'Critical', 'Critical',
 'BUG — Test Trade: ComboBox blanks after selection + InstrumentID=0 rejected by eToro',
 'Two UAT failures on the Test Trade page:

BUG-A: ComboBox blanks after instrument selection (no visual confirmation).
  Root cause: after ApplySelection() sets ContractComboBox.Text and restores
  _isUpdating = False, WPF fires a deferred TextChanged on the editable box.
  The 320 ms search timer fires, treats the display text (e.g. "Oil  [OIL]")
  as a raw search query, finds no exact favourite match, calls the live API,
  gets no result, calls RebuildList(Nothing) which internally resets the
  combobox text — blanking the control.
  Fix: in ApplySelection() and SelectBySymbol(), stop the timer and set
  _lastSearchText = item.ToString() before restoring _isUpdating.  The timer
  tick guard (text = _lastSearchText → Return) then short-circuits any
  deferred TextChanged.
  File: ContractSelectorControl.xaml.vb

BUG-B: Order rejected — eToro 500 because InstrumentID=0 in POST body.
  Confirmed in log: POST .../market-open-orders/by-units → {"InstrumentID":0,...}
  Root cause: TestTradeView.xaml only bound ContractId on ContractSelectorControl
  — InstrumentId DP was never wired to the ViewModel, so Order.InstrumentId
  stayed at its default of 0.
  Fix:
    1. Added TestTradeInstrumentId As Integer property to TestTradeViewModel.
    2. Added InstrumentId="{Binding TestTradeInstrumentId, Mode=TwoWay}" to
       ContractSelectorControl in TestTradeView.xaml.
    3. Added .InstrumentId = _testTradeInstrumentId to Order initialiser in
       ExecuteTestTrade().
  Files: TestTradeViewModel.vb, TestTradeView.xaml',
 'Damo', 3, 'bug,ui,trading,critical', 'Fixed. BUG-A: ContractSelectorControl.xaml.vb. BUG-B: TestTradeViewModel.vb + TestTradeView.xaml.');

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-012', 'Complete', 'Critical', 'High',
 'BUG — by-units order rejected: field serialised as "Units" instead of "AmountInUnits"',
 'eToro API returned 400 Bad Request on POST market-open-orders/by-units:
  "Validation failed: AmountInUnits: ''Amount In Units'' must be greater than ''0''"

Root cause:
  OpenMarketOrderByUnitsRequest had [JsonPropertyName("Units")] on the quantity
  property.  The eToro OpenAPI spec (12_Rest_API_Trading_Demo.txt) and the
  required-fields list both specify "AmountInUnits" as the correct JSON key.
  The serialised body was therefore {"InstrumentID":18,"IsBuy":true,"Leverage":1,"Units":1}
  — the AmountInUnits field was absent so the server treated it as 0 and rejected.

Fix applied:
  PlaceOrderRequest.vb  — renamed property Units → AmountInUnits,
                          changed [JsonPropertyName] to "AmountInUnits".
  OrderService.vb       — updated call site .Units → .AmountInUnits.',
 'Damo', 1, 'bug,trading,api,critical', 'Fixed in PlaceOrderRequest.vb + OrderService.vb');

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-013', 'Complete', 'Critical', 'Critical',
 'BUG — Test Trade: non-standard character in selector + BUY order never reaches eToro',
 'Two UAT failures on the Test Trade page after TICKET-002 refactor:

BUG-A: Instrument selector ComboBox displays a non-standard character prefix on favourites.
  Root cause: the star emoji U+2B50 (UTF-8: E2 AD 90) stored in ContractSelectorControl.xaml.vb
  was double-encoded — the three raw bytes E2, AD, 90 were each individually encoded as
  UTF-8, producing the Latin-1 triplet U+00E2 U+00AD U+0090 (â + soft-hyphen + control char).
  At runtime the compiled string literal contained these three replacement characters, so the
  dropdown items rendered with "â" followed by invisible control characters instead of a star.
  Fix: byte-replaced the garbled sequence with U+2605 (★ BLACK STAR), a BMP character with
  full coverage in standard Windows fonts (Segoe UI, Segoe UI Symbol, Arial, etc.).
  File: ContractSelectorControl.xaml.vb — Display property of InstrumentItem.

BUG-B: BUY 1 SIZE (and SELL 1 SIZE) buttons click without sending any order to eToro.
  Root cause: the TICKET-002 commit rewrote ContractSelectorControl and ExecuteTestTrade but
  did not carry forward three fixes from TICKET-011 / TICKET-012:
  1. TestTradeView.xaml ContractSelectorControl element was missing the InstrumentId two-way
     binding, so TestTradeViewModel.TestTradeInstrumentId remained 0 at order time.
  2. ExecuteTestTrade built the Order without .InstrumentId, so eToro received InstrumentID=0
     and returned a validation error that was silently swallowed.
  3. The TICKET-002 rewrite of SelectBySymbol omitted the _searchTimer.Stop() /
     _lastSearchText anchor that prevents the deferred TextChanged from blanking the ComboBox
     after an instrument is selected — causing TestTradeContractId to revert to "" and
     CanPlaceTestTrade to return False, leaving the BUY button permanently disabled.
  Additionally ExecuteTestTrade still used Quantity=1 (by-units) instead of Amount (by-amount),
  conflicting with the SL/TP percentage logic introduced in the same commit.

Fixes applied:
  ContractSelectorControl.xaml.vb — re-applied _searchTimer.Stop() + _lastSearchText anchor
    in SelectBySymbol (was present in ApplySelection but missing in SelectBySymbol after rewrite).
  TestTradeView.xaml — added InstrumentId="{Binding TestTradeInstrumentId, Mode=TwoWay}" to
    ContractSelectorControl element.
  TestTradeViewModel.vb — added TestTradeInstrumentId As Integer property; added
    .InstrumentId = _testTradeInstrumentId and .Amount = amountVal to Order initialiser in
    ExecuteTestTrade; added SL/TP % calculation using refPrice from live quote or last candle.
  OrderService.vb — updated by-units call site .Units -> .AmountInUnits to match the rename
    in TICKET-012.',
 'GitHub Copilot', 4, 'bug,ui,trading,api,critical',
 'Fixed. BUG-A: ContractSelectorControl.xaml.vb (Display property). BUG-B: ContractSelectorControl.xaml.vb (SelectBySymbol) + TestTradeView.xaml + TestTradeViewModel.vb + OrderService.vb.');

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-014', 'Complete', 'Critical', 'Critical',
 'BUG — Three runtime failures prevent BUY order fill from being tracked',
 'Runtime log analysis after TICKET-013 exposed three further defects:

BUG-A: JsonException on candle volume — order placed without SL/TP.
  eToro candle response returns "volume": null for instrument 17 (OIL).
  CandleDto.Volume was declared As Double (non-nullable). System.Text.Json threw
  JsonException deserialising null → Double, the Catch block swallowed it, refPrice
  stayed 0, and "⚠ Could not resolve price" was logged.  SL/TP were dropped.
  Fix: CandleDto.Volume changed to Double? (nullable).
  HistoryClient.vb: CLng(c.Volume) → CLng(c.Volume.GetValueOrDefault(0)).
  File: BarResponse.vb, HistoryClient.vb

BUG-B: PlaceOrderResponse.OrderId always 0 — GET /orders/0 → 404.
  eToro wraps the order fields inside an "orderForOpen" envelope:
    { "orderForOpen": { "orderID": 333180104, ... }, "token": "..." }
  PlaceOrderResponse had [JsonPropertyName("orderId")] at root level; that key
  does not exist at root so OrderId deserialised as 0.  Downstream:
    order.ExternalOrderId = 0
    ResolvePositionIdAsync → GET /orders/0 → 404
    TryGetOrderFillPriceAsync polling always looked for positionId=0 → no match
    "Strategy: Order fill polling timed out (10s)."
  The actual order (orderID=333180104) WAS accepted by eToro but the app had no
  record of it and could never track, manage or close the position.
  Fix: Added OrderForOpenDto class with [JsonPropertyName("orderID")].
  PlaceOrderResponse now holds OrderForOpen As OrderForOpenDto with a computed
  readonly OrderId property that reads OrderForOpen?.OrderId.
  File: OrderResponse.vb

BUG-C: TryGetOrderFillPriceAsync never matches + wrong fill-price field name.
  (i) Fill polling called SearchOrdersAsync (portfolio) and matched by
      o.Id (= positionId) == externalOrderId (= orderId).  positionId and
      orderId are different values — no match was ever possible.
  (ii) ResolvePositionIdAsync used pos.OpenRate to capture the fill price, but
       GET /orders/{orderId} returns "rate" (not "openRate") for its positions
       array.  The fill price was always recorded as 0.
  Fix: Added OrderPositionDto with [JsonPropertyName("rate")] and
       [JsonPropertyName("positionID")].  OrderInfoResponse.Positions changed
       from List(Of EToroPositionDto) to List(Of OrderPositionDto).
       ResolvePositionIdAsync now reads pos.Rate.
       TryGetOrderFillPriceAsync now calls GetOrderInfoAsync(orderId) and returns
       pos.Rate from the first position — correctly polling the order-specific
       endpoint until the position appears.
  File: OrderResponse.vb, OrderService.vb',
 'GitHub Copilot', 3, 'bug,trading,api,critical',
 'Fixed. BUG-A: BarResponse.vb + HistoryClient.vb. BUG-B: OrderResponse.vb (OrderForOpenDto). BUG-C: OrderResponse.vb (OrderPositionDto) + OrderService.vb.');

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-015', 'Complete', 'Medium', 'Medium',
 'FEATURE — Add Leverage selector to Test Trade page',
 'Add a Leverage ComboBox (values: x1, x2, x5, x10) to the Test Trade settings row,
positioned between the Amount ($) field and the SL % field.

Scope:
1. TestTradeViewModel.vb — added AvailableLeverages As Integer() = {1, 2, 5, 10} and
   TestTradeLeverage As Integer property (default 1).
2. TestTradeView.xaml — Row 2 Grid expanded from 8 → 11 columns; Leverage label +
   ComboBox (UnclippedComboBoxStyle, items displayed as x1/x2/x5/x10) inserted at
   cols 3–4; SL and TP shifted to cols 6–7 and 9–10.
3. TestTradeViewModel.vb ExecuteTestTrade — Order initialiser now includes
   .Leverage = If(_testTradeLeverage > 0, _testTradeLeverage, 1).
   OrderService already forwarded Order.Leverage to both OpenMarketOrderByAmountRequest
   and OpenMarketOrderByUnitsRequest — no service-layer changes required.

UAT — 04/03/2026:
  OIL BUY, Amount $500, Leverage x2, SL 10%, TP 25%.
  eToro portal confirmed:
    Fill @ 75.37, 13.267878 Units invested.
    SL = 67.71  (expected: 75.37 − (10%×75.37/1) = 67.63 ✔ within rounding)
    TP = 94.03  (expected: 75.37 + (25%×75.37/1) = 94.21 ✔ within rounding)
    Net Value $498.53 (−$1.46 / −0.29%) — live P&L tracking active.',
 'GitHub Copilot', 2, 'feature,ui,trading',
 'UAT passed 04/03/2026. Files: TestTradeView.xaml + TestTradeViewModel.vb.');

-- ── Audit Report: EMA/RSI Combined Strategy (2025-07) ──────────────────────────────────────

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-016', 'For Development', 'Critical', 'Critical',
 'AUDIT — RiskGuardService bypassed: engine places orders with no daily-loss or drawdown check',
 'Audit finding (audit-2025-07, Issue #1 CRITICAL).

StrategyExecutionEngine.vb has zero references to IRiskGuardService.
RiskSettings limits (DailyLossLimitDollars = -$1500, MaxDrawdownDollars = -$2000)
are only enforced by AutoExecutionService, which is a separate code path.
The EMA/RSI Combined engine can therefore keep placing real-money trades indefinitely
even after the daily loss or drawdown limit is breached.

Fix required:
1. Add IRiskGuardService parameter to StrategyExecutionEngine constructor.
2. In DoCheckAsync, call _riskGuard.EvaluateRiskAsync(account) immediately
   before calling PlaceBracketOrdersAsync or PlaceScaleInOrderAsync.
   If False is returned, log the halt reason and call Stop().
3. Confirm RiskGuardService is registered in DI (ServicesExtensions.vb) as Singleton.
4. Pass the StrategyDefinition.AccountId to build a minimal Account object for the check.

Files expected to change:
  StrategyExecutionEngine.vb — constructor, DoCheckAsync, EvaluateConfidenceActionsAsync
  ServicesExtensions.vb      — verify IRiskGuardService registration

Acceptance criteria:
  - Engine stops AND logs "Risk limit breached" when daily P&L < -$1500
  - Engine stops AND logs "Risk limit breached" when drawdown < -$2000
  - No order is placed after the halt is set',
 'GitHub Copilot', 4, 'risk,trading,audit,critical', NULL);

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-017', 'Backlog', 'High', 'Medium',
 'AUDIT — Missing indicator signals: Ichimoku, DMI/ADX, MACD, StochRSI not in EMA/RSI Combined',
 'Audit finding (audit-2025-07, Issue #2 HIGH).

etoro TradingView Indicators.txt specifies a 5-indicator Combined Signal Strategy
requiring: EMA21/EMA50, Ichimoku Cloud (Tenkan>Kijun + cloud position), DMI/ADX (14,14)
with DI+/DI- crossover and ADX>25 strength filter, MACD(12/26/9) histogram direction,
and Stochastic RSI (<0.8 long, >0.2 short).

The engine (StrategyExecutionEngine.vb, EmaRsiWeightedScore case) only implements
EMA21, EMA50, and RSI14 — 4 of the 6 documented indicator families are absent.
This means trades fire in low-momentum, ranging, or trend-exhausted markets where
Ichimoku cloud position, ADX<25 or MACD divergence would have vetoed the signal.

Scope (when prioritised):
  TechnicalIndicators.vb — add Ichimoku, DMI/ADX, MACD, StochRSI methods
  StrategyExecutionEngine.vb — integrate new signals into EmaRsiWeightedScore scoring
  The 6 hardcoded weights (25/20/15/20/10/10) must be recalibrated for 10+ signals

Note: TICKET-018 (ADX gate) is a quick win that addresses the most critical gap
(trend-strength guard) without requiring full Ichimoku/MACD implementation.',
 'Damo', 20, 'trading,strategy,audit', 'Blocked: large feature — implement TICKET-018 (ADX gate) first as a quick win');

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-018', 'Backlog', 'High', 'Medium',
 'AUDIT — No multi-timeframe trend filter: engine uses 5-min bars only',
 'Audit finding (audit-2025-07, Issue #3 HIGH).

AIT Overview.docx and Day Trading 101.txt both require multi-timeframe confirmation:
  Daily → big-picture direction
  4-hour → intermediate trend
  1-hour → trend confirmation
  5-minute → entry timing

The engine fetches only 5-minute bars (TimeframeMinutes=5 in ApplyEmaRsiCombined).
No higher-timeframe trend filter exists. A Long signal can therefore fire on 5-min
bullish momentum while the 1-hour or daily trend is strongly bearish — trading
against the dominant trend.

Fix (when prioritised):
  StrategyExecutionEngine.DoCheckAsync — before evaluating EmaRsiWeightedScore,
  fetch the last 50 1-hour bars, compute EMA21/EMA50 on them, and only allow
  a LONG 5-min signal when 1h EMA21 > 1h EMA50 (and vice versa for SHORT).
  Gate is non-blocking: if 1h bars unavailable, log a warning and allow trade.

Files expected to change:
  StrategyExecutionEngine.vb — DoCheckAsync (add 1h bar fetch + EMA trend gate)
  StrategyDefinition.vb — optional: add HigherTimeframeFilterEnabled flag',
 'Damo', 8, 'trading,strategy,audit', NULL);

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-019', 'For Development', 'High', 'Medium',
 'AUDIT — ADX < 25 no-trade gate not implemented (trend strength guard)',
 'Audit finding (audit-2025-07, Issue #3b HIGH).

etoro TradingView Indicators.txt specifies "Strong Trend Criteria: ADX > 25"
as a prerequisite before any entry. The EMA/RSI Combined engine has no ADX
or trend-strength filter. Trades fire equally in trending and ranging markets,
significantly increasing false-positive entries in sideways price action.

This is the highest-priority part of TICKET-017 and is independently implementable
without the full Ichimoku/MACD work.

Fix required:
1. TechnicalIndicators.vb — add DMI(highs, lows, closes, period) function returning
   (+DI, -DI, ADX) arrays using Wilder smoothing (same as RSI).
2. StrategyExecutionEngine.vb (EmaRsiWeightedScore case) — compute ADX(14) on current
   bar series. If ADX < 25, suppress signal and log:
   "ADX={adx:F1} < 25 — low trend strength, signal suppressed ({remStr})"
3. Raise ConfidenceUpdated event even when signal is suppressed (UI score display unaffected).

Acceptance criteria:
  - No LONG or SHORT entry fires when ADX < 25, regardless of confidence score
  - ADX value logged every bar check
  - Existing EMA/RSI scoring weights unchanged (gate is pre-entry, not a weight adjustment)',
 'GitHub Copilot', 5, 'trading,strategy,audit', NULL);

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-020', 'For Development', 'Medium', 'Low',
 'AUDIT — RSI overbought does not amplify SHORT signals (asymmetric scoring)',
 'Audit finding (audit-2025-07, Issue #4 MEDIUM).

In StrategyExecutionEngine.vb (EmaRsiWeightedScore), the RSI gradient adds
up to +20 pts to the bull score when RSI <= 30 (oversold = bullish).
When RSI >= 70 (overbought = bearish per docs), rsiScore = 0 — neutral, not negative.
The TrendAnalysisService.vb handles this symmetrically (RSI>70 → 14 pts bearish,
6 pts bullish; RSI<30 → 14 pts bullish, 6 pts bearish) but is not used by the engine.

Result: an overbought market does not push the confidence score toward SHORT.
A clear downtrend with RSI 75 scores identically to RSI 50 on the RSI signal.

Fix required (StrategyExecutionEngine.vb, EmaRsiWeightedScore case, RSI gradient):
  Replace:
    If rsiVal <= 30 Then rsiScore = 20
    ElseIf rsiVal >= 70 Then rsiScore = 0
    Else rsiScore = (70.0 - rsiVal) / 40.0 * 20.0
  With symmetric bear-aware formula:
    If rsiVal <= 30 Then rsiScore = 20           (oversold  → full bull)
    ElseIf rsiVal >= 70 Then rsiScore = -10      (overbought → active bear contribution)
    Else rsiScore = (50.0 - rsiVal) / 20.0 * 10 (linear: +10 at 30 → 0 at 50 → -10 at 70)

  bullScore is clamped to [0, 100] after all signal additions to prevent overflow.

Files expected to change:
  StrategyExecutionEngine.vb — EmaRsiWeightedScore RSI gradient block (lines ~356–364)',
 'GitHub Copilot', 2, 'trading,strategy,audit', NULL);

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-021', 'For Development', 'Medium', 'Low',
 'AUDIT — Scale-in fires on 30-second timer ticks, not on completed new bars',
 'Audit finding (audit-2025-07, Issue #5 MEDIUM).

In EvaluateConfidenceActionsAsync (StrategyExecutionEngine.vb), the scale-in
consecutive-extreme counter (_extremeConfidenceDurationCount) increments on every
30-second timer tick. On a 5-minute bar, up to 10 ticks occur, so 3 consecutive
extreme ticks = 90 seconds — less than 2 complete bars.

This means all 3 scale-in trades (cap MaxScaleInTrades=3) can fire within a single
5-minute candle ($200 × 3 at 5× = $3,000 notional) if confidence stays extreme.

The reversal counter already uses isNewBar de-duplication for this exact reason.
The scale-in counter should match for consistency and to prevent multi-scale-ins
on the same bar.

Fix required (StrategyExecutionEngine.vb, EvaluateConfidenceActionsAsync):
  The isNewBar flag is computed in DoCheckAsync but not passed into this method.
  Options:
  A) Add isNewBar As Boolean parameter to EvaluateConfidenceActionsAsync and gate
     the counter increment: If isNewBar AndAlso isExtreme Then _extremeConfidenceDurationCount += 1
  B) Track _lastScaleInBarTimestamp As DateTimeOffset and compare to lastBar.Timestamp.

  Option A is minimal; Option B is self-contained.
  Update UI description text + log line to say "3 consecutive new bars" vs "3 ticks".

Files expected to change:
  StrategyExecutionEngine.vb — EvaluateConfidenceActionsAsync signature + counter gate
  AiTradingViewModel.vb      — StrategyNakedDescription text + log briefing line',
 'GitHub Copilot', 3, 'trading,strategy,audit', NULL);

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-022', 'For Development', 'Low', 'Low',
 'AUDIT — StrategyDefaults "EMA/RSI Combined" entry contains dead tick-based values',
 'Audit finding (audit-2025-07, Issue #7 LOW).

StrategyDefaults.vb registers:
  {"EMA/RSI Combined", New StrategyParameterSet("50000", "1", "40", "20")}

Capital=$50,000, TP=40 ticks, SL=20 ticks — these are non-eToro tick-based values
that conflict with the eToro %-based UI defaults (Capital=$200, TP=4%, SL=1.5%).

SelectEmaRsiCombinedCommand calls ApplyEmaRsiCombined() directly; it never reads
StrategyDefaults. The entry is dead code that could mislead future developers or
any code path that calls StrategyDefaults.TryGet("EMA/RSI Combined").

Fix required (StrategyDefaults.vb):
  Update the entry to reflect the eToro-compatible defaults used in ApplyEmaRsiCombined:
  {"EMA/RSI Combined", New StrategyParameterSet("200", "1", "4.0", "1.5")}
  where Capital=200, Qty=1, TP=4.0 (%), SL=1.5 (%)
  Add a comment clarifying these are eToro percentage-based values, not tick offsets.',
 'GitHub Copilot', 1, 'maintenance,trading,audit', NULL);

INSERT OR IGNORE INTO Tickets
    (TicketId, Status, Priority, Severity, Title, Description, AssignedTo, TokenEstimate, Labels, Notes)
VALUES
('TICKET-023', 'Backlog', 'Low', 'Low',
 'AUDIT — TRADING RULES.docx not found in repository (documentation gap)',
 'Audit finding (audit-2025-07, Issue #8 LOW / Doc Gap).

The audit request referenced "TRADING RULES.docx" as a source document for
risk/discipline rules. A full recursive search of the project directory and OneDrive
found no file matching that name. The closest equivalent documents found were:
  - Day Trading Strategies\AIT Overview.docx
  - Day Trading Strategies\Day Trading 101.txt
  - Day Trading Strategies\etoro TradingView Indicators.txt

If TRADING RULES.docx exists elsewhere or has been renamed, its rules have not
been cross-referenced against the engine. Specific rules that could not be verified:
  - Maximum trades per day
  - Maximum concurrent open positions (RiskSettings has MaxPositionSizeContracts=3
    but this is never checked by StrategyExecutionEngine)
  - Time-of-day restrictions (no market-session filter in engine)
  - Post-loss cool-down period

Action required:
  Option A: Locate or create TRADING RULES.docx and add it to
    "Day Trading Strategies\" in the repository.
  Option B: Confirm the relevant rules are fully covered by RiskSettings.vb
    and document the mapping.
  Option C: If no document exists, create a STRATEGY_RULES.md in
    "Day Trading Strategies\" that consolidates the rules from the three
    existing documents into a single reference.',
 'Damo', 2, 'documentation,audit,risk', NULL);

-- Remove any malformed records created with short IDs
DELETE FROM Tickets WHERE TicketId NOT LIKE 'TICKET-%';
