CREATE TABLE properties (
    id            TEXT PRIMARY KEY,
    name          TEXT NOT NULL DEFAULT '',
    address       TEXT NOT NULL DEFAULT '',
    wifi_network  TEXT NOT NULL DEFAULT '',
    wifi_password TEXT NOT NULL DEFAULT '',
    door_code     TEXT NOT NULL DEFAULT '',
    ical_token    TEXT NOT NULL DEFAULT '',
    information TEXT NOT NULL DEFAULT '',
    checkin_procedures TEXT NOT NULL DEFAULT '',
    checkout_procedures TEXT NOT NULL DEFAULT '',
    is_active     INTEGER NOT NULL DEFAULT 1,
    created_at    TEXT NOT NULL DEFAULT (datetime('now')),
    updated_at    TEXT NOT NULL DEFAULT (datetime('now')),
    latitude      REAL,
    longitude     REAL,
    time_zone     TEXT NOT NULL DEFAULT 'America/Los_Angeles',
    max_consecutive_nights   INTEGER,
    min_advance_notice_days  INTEGER,
    max_advance_booking_days INTEGER,
    access_window_days       INTEGER,
    -- 'HH:mm' in this property's own time_zone, not UTC. NULL means inherit the global
    -- setting. Nobody is enforcing these as arrival deadlines -- they exist so a date can be
    -- turned into a real instant for the access window and the iCal feed, which is what keeps
    -- a same-day turnover reading as one stay ending and another starting instead of two
    -- stays overlapping.
    check_in_time            TEXT,
    check_out_time           TEXT
);
