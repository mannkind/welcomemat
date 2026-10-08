CREATE TABLE blackout_dates (
    id          TEXT PRIMARY KEY,
    property_id TEXT REFERENCES properties(id),
    date        TEXT NOT NULL,
    reason      TEXT NOT NULL DEFAULT '',
    created_by  TEXT NOT NULL REFERENCES users(id),
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_blackout_date ON blackout_dates(date);
CREATE INDEX idx_blackout_property ON blackout_dates(property_id);
