-- Task 4: optimistic check (psql write script, like the unsafe scripts).
-- Requires psql variables trip_id, request_id, passenger_id and observed.
-- Read inside the transaction first:
--   SELECT reserved_seats AS observed FROM lab09.trips WHERE id = :'trip_id' \gset
-- This script does not begin or end the transaction: COMMIT on success, ROLLBACK on failure.
INSERT INTO lab09.bookings (request_id, passenger_id, trip_id)
VALUES (:'request_id', :'passenger_id', :'trip_id')
RETURNING id;

UPDATE lab09.trips
SET reserved_seats = reserved_seats + 1
WHERE id = :'trip_id'
  AND status = 'Scheduled'
  AND reserved_seats < capacity
  AND reserved_seats = :observed          -- (1) still the value we read?
RETURNING id, reserved_seats;

\if :ROW_COUNT
\echo Optimistic check passed: reserved_seats was still :observed. COMMIT to keep the booking.
\else
\echo Optimistic check failed: reserved_seats changed or the trip is full/cancelled. ROLLBACK undoes the INSERT; retry with a new read.
\endif
