CREATE TABLE reservations (
    id          TEXT PRIMARY KEY,
    user_id     TEXT NOT NULL REFERENCES users(id),
    property_id TEXT NOT NULL REFERENCES properties(id),
    check_in    TEXT NOT NULL,
    check_out   TEXT NOT NULL,
    notes       TEXT NOT NULL DEFAULT '',
    deleted_at  TEXT,
    modified_at TEXT,
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_reservations_dates ON reservations(check_in, check_out);
CREATE INDEX idx_reservations_deleted ON reservations(deleted_at);
CREATE INDEX idx_reservations_property ON reservations(property_id);
CREATE INDEX idx_reservations_property_checkin_checkout ON reservations(property_id, check_in, check_out);
