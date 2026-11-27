-- Your implementation goes here. Run `bash /lab/run.sh load` after editing.
-- The function runs in the caller's transaction; do not COMMIT inside it.
-- Task 2: conditional update. Fill in the ___ gaps.
CREATE OR REPLACE FUNCTION lab09.book(
    p_request_id text,
    p_passenger_id text,
    p_trip_id text
)
RETURNS bigint
LANGUAGE plpgsql
AS $$
DECLARE
    v_booking_id bigint;
BEGIN
    UPDATE lab09.trips
    SET reserved_seats = reserved_seats + 1
    WHERE id = p_trip_id
      AND status = 'Scheduled'                      -- (1) only scheduled trips
      AND capacity > reserved_seats;                     -- (2) only if a seat is free

    IF NOT FOUND THEN                -- (3) did the UPDATE hit a row?
        RAISE EXCEPTION 'Trip % is full, cancelled or missing', p_trip_id;
    END IF;

    INSERT INTO lab09.bookings (request_id, passenger_id, trip_id)
    VALUES (p_request_id, p_passenger_id, p_trip_id)
    RETURNING id INTO v_booking_id;         -- (4)

    RETURN v_booking_id;
END;
$$;
