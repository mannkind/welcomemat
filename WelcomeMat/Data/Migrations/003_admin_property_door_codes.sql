CREATE TABLE guest_door_codes (
    user_id     TEXT NOT NULL REFERENCES users(id),
    property_id TEXT NOT NULL REFERENCES properties(id),
    door_code   TEXT NOT NULL DEFAULT '',
    PRIMARY KEY (user_id, property_id)
);
