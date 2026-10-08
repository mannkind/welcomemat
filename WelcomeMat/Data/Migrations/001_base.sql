CREATE TABLE users (
    id            TEXT PRIMARY KEY,
    email         TEXT NOT NULL UNIQUE,
    name          TEXT NOT NULL DEFAULT '',
    picture       TEXT NOT NULL DEFAULT '',
    is_admin      INTEGER NOT NULL DEFAULT 0,
    auth_provider TEXT NOT NULL DEFAULT 'oidc',
    calendar_token TEXT NOT NULL DEFAULT '',
    security_version INTEGER NOT NULL DEFAULT 0,
    created_at    TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at    TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE allowlist (
    id         TEXT PRIMARY KEY,
    email      TEXT NOT NULL UNIQUE,
    added_by   TEXT NULL REFERENCES users(id) ON DELETE SET NULL,
    created_at TEXT NOT NULL DEFAULT (datetime('now'))
);

CREATE TABLE settings (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT INTO settings (key, value) VALUES
    ('site_title', 'WelcomeMat');

CREATE TABLE sessions (
    token            TEXT PRIMARY KEY,
    data             BLOB NOT NULL,
    expiry           REAL NOT NULL,
    user_id          TEXT NULL REFERENCES users(id) ON DELETE CASCADE,
    security_version INTEGER NOT NULL DEFAULT 0,
    issued_at        REAL NOT NULL DEFAULT 0
);

CREATE INDEX idx_sessions_expiry ON sessions(expiry);
CREATE INDEX idx_sessions_user_id ON sessions(user_id);
