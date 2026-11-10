-- Your implementation goes here. Run `bash /lab/run.sh load` after editing.
-- The function runs in the caller's transaction; do not COMMIT inside it.
-- Task 3b: table lock. Lock the whole trips table first, then check, update and insert.
CREATE OR REPLACE FUNCTION lab09.book(
    p_request_id text,
    p_passenger_id text,
    p_trip_id text
)
RETURNS bigint
LANGUAGE plpgsql
AS $$
DECLARE
    v_status       text;
    v_capacity     integer;
    v_reserved     integer;
    v_booking_id   bigint;
BEGIN
    LOCK TABLE lab09.trips IN EXCLUSIVE MODE;    -- (1) held until COMMIT/ROLLBACK

    SELECT status, capacity, reserved_seats
    INTO v_status, v_capacity, v_reserved
    FROM lab09.trips
    WHERE id = p_trip_id;                         -- (2) no FOR UPDATE: the table lock already covers it

    IF NOT FOUND
       OR v_status <> 'Scheduled'
       OR v_reserved = v_capacity THEN
        RAISE EXCEPTION 'Trip % is full, cancelled or missing', p_trip_id;
    END IF;

    UPDATE lab09.trips
    SET reserved_seats = reserved_seats + 1
    WHERE id = p_trip_id;

    INSERT INTO lab09.bookings (request_id, passenger_id, trip_id)
    VALUES (p_request_id, p_passenger_id, p_trip_id)
    RETURNING id INTO v_booking_id;

    RETURN v_booking_id;
END;
$$;
