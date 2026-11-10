-- Final implementation of lab09.book. Run `bash /lab/run.sh load` after editing.
-- The function runs in the caller's transaction; do not COMMIT inside it.
-- Task 6: idempotent booking. The unique constraint on request_id decides whether a request is new;
-- a new request takes a seat with the conditional update from task 2.
CREATE OR REPLACE FUNCTION lab09.book(
    p_request_id text,
    p_passenger_id text,
    p_trip_id text
)
RETURNS bigint
LANGUAGE plpgsql
AS $$
DECLARE
    v_booking_id   bigint;
    v_existing_id  bigint;
    v_passenger    text;
    v_trip         text;
BEGIN
    INSERT INTO lab09.bookings (request_id, passenger_id, trip_id)
    VALUES (p_request_id, p_passenger_id, p_trip_id)
    ON CONFLICT (request_id) DO NOTHING                  -- (1) an existing request inserts nothing
    RETURNING id INTO v_booking_id;

    IF FOUND THEN                                        -- (2) the row was inserted: new request
        UPDATE lab09.trips
        SET reserved_seats = reserved_seats + 1
        WHERE id = p_trip_id
          AND status = 'Scheduled'
          AND reserved_seats < capacity;

        IF NOT FOUND THEN
            -- The exception also rolls back the INSERT above.
            RAISE EXCEPTION 'Trip % is full, cancelled or missing', p_trip_id;
        END IF;

        RETURN v_booking_id;
    END IF;

    -- Known request: return the original booking if the details match.
    SELECT id, passenger_id, trip_id
    INTO v_existing_id, v_passenger, v_trip
    FROM lab09.bookings
    WHERE request_id = p_request_id;

    IF v_passenger <> p_passenger_id OR v_trip <> p_trip_id THEN   -- (3)
        RAISE EXCEPTION 'Request % is already used for another booking', p_request_id;
    END IF;

    RETURN v_existing_id;                                -- replay: same ID, no extra seat
END;
$$;
