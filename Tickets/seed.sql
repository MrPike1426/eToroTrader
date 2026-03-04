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

-- Dependency cross-links
UPDATE Tickets SET Blocks    = 'TICKET-005' WHERE TicketId = 'TICKET-001';
UPDATE Tickets SET BlockedBy = 'TICKET-001' WHERE TicketId = 'TICKET-005';
UPDATE Tickets SET Blocks    = 'TICKET-006' WHERE TicketId = 'TICKET-005';
UPDATE Tickets SET BlockedBy = 'TICKET-005' WHERE TicketId = 'TICKET-006';
UPDATE Tickets SET Blocks    = 'TICKET-007' WHERE TicketId = 'TICKET-002';
UPDATE Tickets SET BlockedBy = 'TICKET-002' WHERE TicketId = 'TICKET-007';
UPDATE Tickets SET BlockedBy = 'TICKET-001,TICKET-003' WHERE TicketId = 'TICKET-008';

-- Remove any malformed records created with short IDs
DELETE FROM Tickets WHERE TicketId NOT LIKE 'TICKET-%';
