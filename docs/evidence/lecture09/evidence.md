# Transactions: The last seat

Reset the fixture between runs, after both sessions have ended their transactions.
Order notation: `A:read → B:read → A:write → A:commit → B:write → B:commit`.
`(waits)` marks a statement that blocked until the other session ended its transaction.

## Experiments

| # | Task / variant | Isolation | Trip | Order (A/B) | Who waits, on what | Error / SQLSTATE | Final bookings | reserved_seats |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 / unsafe read-then-write | Read Committed | T-LAST | A:read → B:read → A:write → A:commit → B:write → B:commit | Nobody (A committed before B wrote) | None | 2 (unsafe-A, unsafe-B) | 1 (`counts_agree = f`) |
| 2 | 1 / `variants/01-unsafe-increment.sql` (`reserved_seats + 1`) | Read Committed | T-LAST | A:read → B:read → A:write → A:commit → B:write (fails) → B:rollback | Nobody (A committed before B wrote) | B UPDATE: `23514` check violation `trips_check`, failing row `(T-LAST, Scheduled, 1, 2)` | 1 (inc-A) | 1 (`counts_agree = t`) |
| 3 | 2 / `variants/02-conditional.sql`, A commits | Read Committed | T-LAST | A:book → B:book (waits) → A:commit → B:error → B:rollback | B's UPDATE waits for A's row lock (`Lock/transactionid`, blocked by A) until A commits | B: `P0001` "Trip T-LAST is full, cancelled or missing" | 1 (request-A, id 1) | 1 (`counts_agree = t`) |
| 4 | 2 / `variants/02-conditional.sql`, A rolls back | Read Committed | T-LAST | A:book → B:book (waits) → A:rollback → B:returns 2 → B:commit | B's UPDATE waits for A's row lock until A rolls back | None | 1 (request-B, id 2) | 1 (`counts_agree = t`) |
| 5 | 3a / `variants/03-row-lock.sql`, A commits | Read Committed | T-LAST | A:book → B:book (waits) → A:commit → B:error → B:rollback | B's `SELECT ... FOR UPDATE` waits for A's row lock (`Lock/transactionid`) | B: `P0001` full, cancelled or missing | 1 (request-A) | 1 (`counts_agree = t`) |
| 6 | 3a / `variants/03-row-lock.sql`, A rolls back | Read Committed | T-LAST | A:book → B:book (waits) → A:rollback → B:returns 2 → B:commit | B's `SELECT ... FOR UPDATE` waits until A rolls back | None | 1 (request-B) | 1 (`counts_agree = t`) |
| 7 | 3a / `variants/03-row-lock.sql`, other trip | Read Committed | T-LAST, T-OTHER | A:book T-LAST → B:book T-OTHER → B:commit → B:book T-LAST (waits) → A:commit → B:error → B:rollback | T-OTHER: nobody. T-LAST: B waits for A | B on T-LAST: `P0001` | T-LAST 1 (request-A), T-OTHER 1 (request-B-other) | T-LAST 1, T-OTHER 1 (all agree) |
| 8 | 3b / `variants/04-table-lock.sql`, A commits | Read Committed | T-LAST | A:book → B:book (waits) → A:commit → B:error → B:rollback | B waits for the table lock (`Lock/relation`) | B: `P0001` | 1 (request-A) | 1 (`counts_agree = t`) |
| 9 | 3b / `variants/04-table-lock.sql`, A rolls back | Read Committed | T-LAST | A:book → B:book (waits) → A:rollback → B:returns 2 → B:commit | B waits for the table lock (`Lock/relation`) | None | 1 (request-B) | 1 (`counts_agree = t`) |
| 10 | 3b / `variants/04-table-lock.sql`, other trip | Read Committed | T-LAST, T-OTHER | A:book T-LAST → B:book T-OTHER (waits) → A:commit → B:returns 2 → B:commit | B waits on T-OTHER although A only booked T-LAST (`Lock/relation`); plain SELECT from C does not wait | None | T-LAST 1, T-OTHER 1 | T-LAST 1, T-OTHER 1 (all agree) |

## 1. Double booking

### Prediction

- Both sessions read `reserved_seats = 0` (A has not written yet when B reads).
- B writes `reserved_seats = 1` (its own stale `0 + 1`), overwriting A's 1.
- Two committed bookings, `reserved_seats = 1`.

### Result

Prediction confirmed (experiment #1).

- A and B both printed `Observed reserved_seats=0; can_book=t`.
- A: `UPDATE 1`, booking id 1, `COMMIT`. B: `UPDATE 1`, booking id 2, `COMMIT`. Neither statement blocked.
- `inspect.sql`: T-LAST `reserved_seats = 1`, `booking_count = 2`, `counts_agree = f`.
- Note: a first attempt ran in autocommit because a multi-line paste merged `BEGIN` into
  `\set passenger_id` (`passenger-ABEGINISOLATIONLEVELREADCOMMITTED;`). Same end state, but no
  transactions were open, so it was discarded and rerun.

### Answers

- Stale value B writes back: `reserved_seats = 1`, computed as `:seen_reserved + 1 = 0 + 1`.
  `seen_reserved` was read before A wrote, so B overwrote A's 1 with its own 1 (lost update).
- Why two bookings can coexist with a count of one: the counter and the booking rows are
  separate. Each transaction inserted its own booking row and set the counter to an absolute
  value. Nothing links the number of rows to the counter, so both commits succeed.
- Why BEGIN and COMMIT did not prevent it: a transaction makes A's UPDATE and INSERT atomic
  (all or nothing). It does not stop another session from acting on a value it read earlier.
  At Read Committed a plain `SELECT` takes no lock and is not rechecked at commit. A's UPDATE
  did take a row lock, but only until A committed. If B had written while A was still open,
  B would have waited for that lock and then still written `0 + 1`, because its decision was
  made at read time. The lock changes when B writes, not what it writes.
- Why the CHECK constraint accepted it: a CHECK only sees the row being written, in this case
  `reserved_seats = 1` and `capacity = 1`, which is valid. It cannot see `lab09.bookings`, so it
  cannot detect that two bookings exist for a counter of one.

### Constraint experiment (`reserved_seats = reserved_seats + 1`)

Prediction: B writes 2 and fails. Confirmed (experiment #2).

- Error / SQLSTATE: B's UPDATE failed with `23514` (check_violation) on `trips_check`,
  failing row `(T-LAST, Scheduled, 1, 2)`. B still read `0` / `can_book=t`, but
  `reserved_seats + 1` uses the row's current value (A's committed 1), not B's stale read.
  The following INSERT got `25P02` (transaction aborted) until B ran `ROLLBACK`.
  After rollback: 1 booking, `reserved_seats = 1`, `counts_agree = t`.
- Why the result changed: the only difference from `unsafe-write.sql` is
  `:seen_reserved + 1` (psql variable, client computes a fixed value from the stale read) vs.
  `reserved_seats + 1` (column, the database computes from the row's current value at write time).
- What the constraint protects: only the single `trips` row (`0 <= reserved_seats <= capacity`),
  not its relationship to `lab09.bookings`.
- Does it prove booking rows agree with the counter? No. Here they agree because B's INSERT and
  UPDATE were in the same transaction, so B's rollback removed both. A booking inserted without
  touching `trips` would pass every constraint. B simply lost the last seat to A.
- What if the booking insert committed separately? B's booking would stay committed while its
  UPDATE failed: two bookings, `reserved_seats = 1`, i.e. the same overbooking as experiment #1.
  Insert and counter change must be in one transaction.

## 2. Conditional update

Implementation: `sql/book.sql`, saved as `sql/variants/02-conditional.sql`. One
`UPDATE ... WHERE id = p_trip_id AND status = 'Scheduled' AND capacity > reserved_seats`,
then `IF NOT FOUND THEN RAISE EXCEPTION`, then the booking INSERT `RETURNING id INTO v_booking_id`.
A rejection is an exception, so the caller's transaction cannot commit a half booking.

### Prediction

- (a) A holds T-LAST open; B's call waits, because A's UPDATE locks the row until A ends.
- (b) After A commits, B continues.
- (c) After A rolls back, B is free to continue.

### Result

Experiments #3 and #4. Waiting confirmed: while A was open, B was `active` with
`wait=Lock/transactionid` and `pg_blocking_pids` = A.

- A commits (#3): B's UPDATE resumed, found 0 rows and raised `P0001`. B rolled back.
  Final: 1 booking (request-A), `reserved_seats = 1`.
- A rolls back (#4): B's UPDATE resumed, matched the row and returned booking id 2. B committed.
  Final: 1 booking (request-B), `reserved_seats = 1`.
- Booking id 2 in #4 although A rolled back: identity/sequence values are not returned on rollback,
  so gaps in IDs are normal.

### Answers

- Which statement makes the capacity decision? The UPDATE itself, through its `WHERE`: the trip
  must exist (`id = p_trip_id`), be `Scheduled` and have `capacity > reserved_seats`. Check and
  write happen in one statement on the locked, current row version, so nobody can change the row
  in between.
- Why is reading availability first insufficient? A plain `SELECT` takes no lock, so the value
  can be stale by the time the UPDATE runs: another transaction can commit in between. That is
  exactly what happened to B in experiment #1.
- Does a conditional update avoid waiting for another writer? No. In #3 and #4 B waited for A's
  row lock. What it avoids is a wrong result: after the wait, PostgreSQL re-evaluates the `WHERE`
  against the newly committed row version (#3: `reserved_seats = 1`, so 0 rows). Only the T-LAST
  row is locked; a booking on another trip would not wait.
- What can a zero-row result tell you, and what can it not distinguish? It tells you that no row
  satisfied all conditions. It cannot tell whether the trip is missing, cancelled or full, hence
  the combined message. A precise reason needs an extra lookup after the failure.
- Why check `FOUND` immediately? `FOUND` is overwritten by every later statement (`SELECT INTO`,
  `INSERT`, `PERFORM`, ...). A statement in between would make the IF test that statement's result,
  not the UPDATE's. Errors are not the concern: a failing UPDATE raises an exception and never
  reaches the IF.

## 3. Explicit locks

### Row lock (`SELECT ... FOR UPDATE`)

Implementation: `sql/variants/03-row-lock.sql`. `SELECT status, capacity, reserved_seats INTO ...
FOR UPDATE` locks the trip row first; the check is done in PL/pgSQL (`IF NOT FOUND OR v_status <>
'Scheduled' OR v_reserved = v_capacity THEN RAISE`), and the UPDATE only needs `WHERE id = p_trip_id`.
Once the row is locked, no other transaction can change it before we commit, so the value read is
still current at the UPDATE. Unlike experiment #1, read-check-write is safe here, but only as long as
every booking path uses `FOR UPDATE`; a plain `SELECT` still reads without waiting.

Prediction: (a) B waits at the `SELECT ... FOR UPDATE`; (b) after A commits, B sees
`reserved_seats = capacity` and fails; (c) B is not blocked on T-OTHER, only the T-LAST row is locked.

Result (experiments #5–#7): all three confirmed.

- #5 / #6: same outcome as the conditional update (#3 / #4). B waited (`Lock/transactionid`, blocked
  by A) and then failed with `P0001` (A committed) or got the seat (A rolled back).
- #7: B's T-OTHER booking returned at once while A held T-LAST. B's next call on T-LAST waited until
  A committed and then failed.
- From outside, `pg_stat_activity` only shows the `book(...)` call, not the statement inside it that
  waits. That it is the `SELECT ... FOR UPDATE` follows from the code (first statement touching the
  row) and from B getting the RAISE message after the wait, i.e. its SELECT returned
  `reserved_seats = 1`.

Extra check while A held the T-LAST lock (uncommitted booking): a plain
`SELECT reserved_seats FROM lab09.trips WHERE id = 'T-LAST'` from a third session C returned
`0` in 37 ms without waiting. `SELECT ... FOR UPDATE NOWAIT` from C failed at once with
"could not obtain lock on row in relation "trips"".

Answers:

- Which operations wait? Only operations that want to lock or write the same row:
  `SELECT ... FOR UPDATE` and UPDATE on T-LAST. Other rows (T-OTHER, #7) do not wait, and plain
  reads do not wait either (MVCC: readers do not block on writers). The reader sees the last
  committed value (0), not A's uncommitted 1.
- How does the time between acquiring the lock and committing affect other passengers? Everyone
  booking the same trip queues behind the lock for that whole time. If the application held the lock
  during a slow external call (e.g. a 10 s payment), every booking on that trip would wait up to 10 s.
  Under load, waiting connections pile up and may hit timeouts or exhaust the connection pool.
  Bookings on other trips are unaffected. Keep the locked section short and make no external calls
  inside it. Paying before booking risks charging for a seat that is gone, so a common pattern is:
  reserve the seat in a short transaction, pay outside any transaction, then confirm or release the
  seat in another short transaction.

### Table lock (`LOCK TABLE ... IN EXCLUSIVE MODE`)

Implementation: `sql/variants/04-table-lock.sql`. `LOCK TABLE lab09.trips IN EXCLUSIVE MODE` as the
first statement, held until commit or rollback. The SELECT no longer needs `FOR UPDATE`: no other
transaction can write or lock rows in the table while we hold it, so the row lock would be redundant.

Prediction: (a) B must wait to book T-OTHER, because the whole table is locked; (b) a plain SELECT
still goes through, because it only reads.

Result (experiments #8–#10): both confirmed.

- #8 / #9: same outcomes as the row lock (#5 / #6), but B now waits on `Lock/relation` (the table)
  instead of `Lock/transactionid` (the row holder).
- #10: B waited on T-OTHER although A only booked T-LAST; B got its seat right after A committed.
  A plain SELECT from C returned T-LAST `reserved_seats = 0` without waiting: `EXCLUSIVE` conflicts
  with row locks and writes, but not with `ACCESS SHARE` (plain reads). `ACCESS EXCLUSIVE` would also
  block reads.

### Advisory lock (`pg_advisory_xact_lock`)

### Deadlock

## 4. Optimistic check

### Prediction

### Result

### Answers

## 5. Isolation levels

### Repeatable Read

### Serializable

### Answers

### Handling 40001

## 6. Repeated requests

### Prediction

### Result

### Answers

## 7. AI review

- Prompt:
- Model:
- Relevant response:
- Our prediction:
- Actual output:
- Accepted / rejected, and why:
- Conclusion:
