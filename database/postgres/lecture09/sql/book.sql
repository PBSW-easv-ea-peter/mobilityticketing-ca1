-- Your implementation goes here. Run `bash /lab/run.sh load` after editing.
-- The function runs in the caller's transaction; do not COMMIT inside it.
-- Task 3c: transaction advisory lock. Namespace 9009, one key per fixture trip.
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
    v_lock_key     integer;
BEGIN
    v_lock_key := CASE p_trip_id
                      WHEN 'T-LAST'      THEN 1
                      WHEN 'T-TWO'       THEN 2          -- (1)
                      WHEN 'T-CANCELLED' THEN 3
                      WHEN 'T-OTHER'     THEN 4          -- (2)
                  END;
    PERFORM pg_advisory_xact_lock(9009, v_lock_key);     -- (3) released at COMMIT/ROLLBACK

    SELECT status, capacity, reserved_seats
    INTO v_status, v_capacity, v_reserved
    FROM lab09.trips
    WHERE id = p_trip_id;                                -- no FOR UPDATE: the advisory lock serializes bookings

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
