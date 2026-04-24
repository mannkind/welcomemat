CREATE TABLE walking_paths (
    id          TEXT PRIMARY KEY,
    property_id TEXT NOT NULL REFERENCES properties(id) ON DELETE CASCADE,
    name        TEXT NOT NULL,
    color       TEXT NOT NULL DEFAULT '#3388ff',
    weight      INTEGER NOT NULL DEFAULT 3,
    opacity     REAL NOT NULL DEFAULT 1.0,
    coordinates TEXT NOT NULL DEFAULT '[]',
    sort_order  INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE INDEX idx_walking_paths_property ON walking_paths(property_id);
