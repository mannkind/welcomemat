CREATE TABLE guestbook_entries (
    id             TEXT PRIMARY KEY,
    reservation_id TEXT NOT NULL REFERENCES reservations(id) ON DELETE CASCADE,
    property_id    TEXT NOT NULL REFERENCES properties(id) ON DELETE CASCADE,
    user_id        TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    message        TEXT NOT NULL DEFAULT '',
    created_at     TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at     TEXT
);

CREATE UNIQUE INDEX uq_guestbook_reservation ON guestbook_entries(reservation_id);
CREATE INDEX idx_guestbook_property ON guestbook_entries(property_id);
