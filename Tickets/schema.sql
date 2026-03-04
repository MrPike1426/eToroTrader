-- ============================================================
-- eToroTrader Ticket Management Schema  v1.0
-- SQLite — apply via: dotnet run --project DbQuery -- init
-- ============================================================

CREATE TABLE IF NOT EXISTS Tickets (
    TicketId               TEXT    NOT NULL PRIMARY KEY,  -- e.g. TICKET-001
    Status                 TEXT    NOT NULL DEFAULT 'Backlog',
    -- Backlog | For Development | In Development | SIT Testing | Complete | Cancelled
    Priority               TEXT    NOT NULL DEFAULT 'Medium',
    -- Critical | High | Medium | Low
    Severity               TEXT    NOT NULL DEFAULT 'Medium',
    -- Critical | High | Medium | Low
    Title                  TEXT    NOT NULL,
    Description            TEXT,
    AssignedTo             TEXT,
    DueDate                TEXT,                          -- YYYY-MM-DD
    StartDate              TEXT,                          -- YYYY-MM-DD (when dev began)
    TargetCompletionDate   TEXT,                          -- YYYY-MM-DD (dev estimate)
    TokenEstimate          INTEGER NOT NULL DEFAULT 0,
    TokensBurned           INTEGER NOT NULL DEFAULT 0,
    Labels                 TEXT,                          -- comma-separated tags
    Notes                  TEXT,
    Attempts               INTEGER NOT NULL DEFAULT 0,
    BlockedBy              TEXT,                          -- comma-separated TicketIds
    Blocks                 TEXT,                          -- comma-separated TicketIds
    CreatedAt              TEXT    NOT NULL DEFAULT (datetime('now','utc')),
    LastUpdated            TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);

CREATE TABLE IF NOT EXISTS TicketComments (
    CommentId   INTEGER PRIMARY KEY AUTOINCREMENT,
    TicketId    TEXT    NOT NULL REFERENCES Tickets(TicketId) ON DELETE CASCADE,
    Author      TEXT,
    Body        TEXT    NOT NULL,
    CreatedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);

CREATE TABLE IF NOT EXISTS TicketHistory (
    HistoryId   INTEGER PRIMARY KEY AUTOINCREMENT,
    TicketId    TEXT    NOT NULL REFERENCES Tickets(TicketId) ON DELETE CASCADE,
    Field       TEXT    NOT NULL,
    OldValue    TEXT,
    NewValue    TEXT,
    ChangedBy   TEXT,
    ChangedAt   TEXT    NOT NULL DEFAULT (datetime('now','utc'))
);

-- ── Indexes ──────────────────────────────────────────────────────────────────
CREATE INDEX IF NOT EXISTS idx_tickets_status   ON Tickets(Status);
CREATE INDEX IF NOT EXISTS idx_tickets_priority ON Tickets(Priority);
CREATE INDEX IF NOT EXISTS idx_comments_ticket  ON TicketComments(TicketId);
CREATE INDEX IF NOT EXISTS idx_history_ticket   ON TicketHistory(TicketId);

-- ── Triggers — auto-log field changes & update LastUpdated ───────────────────
CREATE TRIGGER IF NOT EXISTS trg_tickets_updated
AFTER UPDATE ON Tickets
BEGIN
    UPDATE Tickets SET LastUpdated = datetime('now','utc') WHERE TicketId = NEW.TicketId;
END;

CREATE TRIGGER IF NOT EXISTS trg_log_status_change
AFTER UPDATE OF Status ON Tickets
WHEN OLD.Status <> NEW.Status
BEGIN
    INSERT INTO TicketHistory(TicketId, Field, OldValue, NewValue)
    VALUES (NEW.TicketId, 'Status', OLD.Status, NEW.Status);
END;

CREATE TRIGGER IF NOT EXISTS trg_log_assignee_change
AFTER UPDATE OF AssignedTo ON Tickets
WHEN OLD.AssignedTo IS NOT NEW.AssignedTo
BEGIN
    INSERT INTO TicketHistory(TicketId, Field, OldValue, NewValue)
    VALUES (NEW.TicketId, 'AssignedTo', OLD.AssignedTo, NEW.AssignedTo);
END;
