CREATE TABLE favorite_spots (
    id          TEXT PRIMARY KEY,
    property_id TEXT NOT NULL REFERENCES properties(id),
    name        TEXT NOT NULL,
    address     TEXT NOT NULL DEFAULT '',
    description TEXT NOT NULL DEFAULT '',
    latitude    REAL,
    longitude   REAL,
    sort_order  INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_favorite_spots_property ON favorite_spots(property_id);
