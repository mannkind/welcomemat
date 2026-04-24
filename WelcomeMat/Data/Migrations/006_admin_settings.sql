INSERT INTO settings (key, value) VALUES
    ('max_consecutive_nights', '7'),
    ('min_advance_notice_days', '1'),
    ('max_advance_booking_days', '90'),
    ('check_in_time', '15:00'),
    ('check_out_time', '11:00'),
    ('admin_ical_token', lower(hex(randomblob(16))));
