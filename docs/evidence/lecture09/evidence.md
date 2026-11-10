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
| 11 | 3c / `variants/05-advisory-lock.sql`, A commits | Read Committed | T-LAST | A:book → B:book (waits) → A:commit → B:error → B:rollback | B waits for the advisory lock (`Lock/advisory`, key 9009/1) | B: `P0001` | 1 (request-A) | 1 (`counts_agree = t`) |
| 12 | 3c / `variants/05-advisory-lock.sql`, A rolls back | Read Committed | T-LAST | A:book → B:book (waits) → A:rollback → B:returns 2 → B:commit | B waits for the advisory lock (`Lock/advisory`) | None | 1 (request-B) | 1 (`counts_agree = t`) |
| 13 | 3c / A uses `book()` (advisory), B uses unsafe path | Read Committed | T-LAST | A:book → B:read → B:write (waits) → A:commit → B:write `0 + 1` → B:commit | B's read: nobody. B's UPDATE waits on A's row lock (`Lock/transactionid`), not the advisory lock | None | 2 (request-A, unsafe-B) | 1 (`counts_agree = f`) |
| 14 | 3d / deadlock, opposite order (plain `SELECT ... FOR UPDATE`) | Read Committed | T-LAST, T-OTHER | A:lock LAST → B:lock OTHER → A:lock OTHER (waits) → B:lock LAST → B:error → A:continues → both rollback | A waits for B on T-OTHER; B would wait for A on T-LAST (cycle) | B: `40P01` deadlock detected | 0 (no bookings) | unchanged |
| 15 | 3d / same order (both T-LAST → T-OTHER) | Read Committed | T-LAST, T-OTHER | A:lock LAST → B:lock LAST (waits) → A:lock OTHER → A:rollback → B:lock LAST → B:lock OTHER → B:rollback | B waits for A on T-LAST only | None | 0 (no bookings) | unchanged |
| 16 | 4 / `variants/06-optimistic-write.sql` | Read Committed | T-TWO | A:read 0 → B:read 0 → A:write → A:commit → B:write (0 rows) → B:rollback → B:new tx read 1 → B:write → B:commit | Nobody (A committed before B wrote) | None; B's first check updated 0 rows | 2 (opt-a, opt-b-retry) | 2 (`counts_agree = t`) |
| 17 | 4 / optimistic, A commits | Read Committed | T-LAST | A:read 0 → B:read 0 → A:write → B:write (waits) → A:commit → B:0 rows → B:rollback → B:retry read 1 → B:0 rows → B:rollback | B's UPDATE waits on A's row lock (`Lock/transactionid`) | None; both B attempts updated 0 rows | 1 (opt-a) | 1 (`counts_agree = t`) |
| 18 | 4 / optimistic, A rolls back | Read Committed | T-LAST | A:read 0 → B:read 0 → A:write → B:write (waits) → A:rollback → B:check passes → B:commit | B's UPDATE waits on A's row lock (`Lock/transactionid`) | None | 1 (opt-b) | 1 (`counts_agree = t`) |
| 19 | 5 / unsafe scripts | Repeatable Read | T-LAST | A:read 0 → B:read 0 → A:write → A:commit → B:write (fails) → B:rollback | Nobody (A committed before B wrote) | B UPDATE: `40001` could not serialize access due to concurrent update (then `25P02` on INSERT) | 1 (iso-a) | 1 (`counts_agree = t`) |
| 20 | 5 / retry of #19 | Repeatable Read | T-LAST | B:new tx → read 1, `can_book=f` → no write → commit | Nobody | None | 1 (iso-a) | 1 (`counts_agree = t`) |
| 21 | 5 / unsafe scripts | Serializable | T-LAST | A:read 0 → B:read 0 → A:write → A:commit → B:write (fails) → B:rollback | Nobody (A committed before B wrote) | B UPDATE: `40001` could not serialize access due to concurrent update (then `25P02` on INSERT) | 1 (iso-a) | 1 (`counts_agree = t`) |
| 22 | 5 / retry of #21 (whole transaction) | Serializable | T-LAST | B:new tx → read 1, `can_book=f` → no write → commit | Nobody | None | 1 (iso-a) | 1 (`counts_agree = t`) |

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

Answer: what concurrency do you lose? Passengers on different trips no longer book in parallel.
With the row lock, B's T-OTHER booking returned at once (#7); with the table lock it waited for A (#10).
Every booking in the system is serialized, one at a time regardless of how many trips exist: 50
passengers booking 50 different trips queue behind each other. One slow transaction (e.g. a payment
inside the lock) now blocks bookings on every trip, not just its own. The row lock gives the same
correctness for this rule with far less waiting.

### Advisory lock (`pg_advisory_xact_lock`)

Implementation: `sql/variants/05-advisory-lock.sql`. A `CASE` maps the trip to its key
(`T-LAST = 1`, `T-TWO = 2`, `T-CANCELLED = 3`, `T-OTHER = 4`), then
`PERFORM pg_advisory_xact_lock(9009, v_lock_key)` before the SELECT (no `FOR UPDATE`). The lock is
released automatically at commit or rollback.

Prediction (unsafe path in B): B can read but not write while A holds the advisory lock; B reads the
old value. Final count: probably 1 reserved seat.

Result (experiments #11–#13):

- #11 / #12: same outcomes as the row lock, but B waits on `Lock/advisory`. `pg_locks` showed A
  holding `advisory classid=9009 objid=1 granted=true`.
- #13: B's unsafe read did not wait and saw `reserved_seats=0`. B's UPDATE did wait, but on A's
  ordinary row lock from the UPDATE inside `book()` (`Lock/transactionid`), not on the advisory lock.
  When A committed, B wrote its stale `SET reserved_seats = 0 + 1` and committed: 2 bookings,
  `reserved_seats = 1`, `counts_agree = f`. The same lost update as experiment #1.
- Prediction partly confirmed: the read went through with the old value and the count ended at 1, but
  B was not stopped from writing, only delayed, and a second booking was created.

Answers:

- Can the lock protect code that ignores the convention? No. An advisory lock is an agreement
  between the code paths that take it. PostgreSQL does not know that key 9009/1 means T-LAST, so the
  unsafe path read and wrote without asking, which gave the double booking in #13.
- Advisory-lock waiting vs. row locks taken by updates: the advisory lock (`Lock/advisory`) is only
  taken explicitly and only serializes callers that request the same key. The row lock
  (`Lock/transactionid`) is taken automatically by every UPDATE, whatever the code does, and stops two
  concurrent writes to the same row. It cannot stop a stale value from being written after the lock is
  released (#13).
- Why use an advisory lock at all? It can protect something that is not a single row, e.g. a rule
  across many rows such as "max 3 bookings per passenger per day". The price is that every code path
  must follow the convention.

### Deadlock

Plain row locks, no booking function: each session runs `SELECT id FROM lab09.trips WHERE id = ...
FOR UPDATE` on two trips.

Prediction (same order): B waits for A at T-LAST; the same row cannot be locked twice.

Result (experiments #14–#15):

- #14, opposite order: A locked T-LAST, B locked T-OTHER, A waited for T-OTHER, then B asked for
  T-LAST. B failed with `40P01: deadlock detected`
  (`Process 37892 waits for ShareLock on transaction 924; blocked by process 37893. Process 37893 waits
  for ShareLock on transaction 925; blocked by process 37892.`). PostgreSQL aborted one transaction in
  the cycle (B); A's waiting statement then returned at once.
- #15, same order: B waited for A at T-LAST. When A ended, B got T-LAST and T-OTHER without waiting
  again, because A releases all its locks together at commit or rollback. No deadlock.
- Prediction confirmed, with one correction: B does not wait a second time at T-OTHER.

Answer: what should the application retry after a deadlock? The whole transaction, from `BEGIN`.
After `40P01` the transaction is aborted and all its work is gone, so retrying only the failed statement
is impossible; the earlier reads may also be stale. Use a small retry limit with a short random delay.
Prevention: acquire locks in a consistent order (e.g. sorted by trip id), which turned #14 into #15.

## 4. Optimistic check

Implementation: `sql/variants/06-optimistic-write.sql`, a psql write script so A and B can be stepped
separately. Each session reads `SELECT reserved_seats AS observed ... \gset` inside its transaction.
The script inserts the booking, then runs the UPDATE with `status = 'Scheduled' AND reserved_seats <
capacity AND reserved_seats = :observed`. `:ROW_COUNT = 0` means the check failed; the session rolls
back, which also undoes the INSERT.

### Prediction

- T-TWO: B's first attempt fails, but there can still be a seat at the retry, because an optimistic
  check is not a capacity check.
- T-LAST: the database still blocks two writers on the same row, not necessarily a "real" lock.

### Result

Experiments #16–#18.

- #16 (T-TWO): A and B both observed 0. A committed (1). B's UPDATE matched 0 rows, because the row
  was 1, not 0, although a seat was still free. B rolled back, read again (observed 1) and succeeded
  (2/2). Booking id 2 is missing: B's rolled-back INSERT used it.
- #17 (T-LAST, A commits): B's UPDATE waited on A's row lock (`Lock/transactionid`). After A
  committed, B's check failed; the retry observed 1 and failed again, now because the trip was full.
- #18 (T-LAST, A rolls back): B waited, then its check passed (the row was still 0) and B got the seat.
- Prediction 1 confirmed in outcome, corrected in reasoning: in #16 A did not fail, it committed. B's
  check failed because the value changed, not because the trip was full. A failed optimistic check
  means "someone changed the row", not "no seat".
- Prediction 2 confirmed: it is a real row lock. "Optimistic" means no lock is taken at read time; the
  UPDATE still locks the row and waits for an open writer.

### Answers

- Why a failed optimistic check does not mean the trip is full: it only says the row no longer has
  the value we read. In #16 one seat was still free, and the retry with a new read got it.
- Waiting and retries compared with the conditional update (task 2): both wait the same way on an open
  writer's row lock (#3/#4 vs. #17/#18). The conditional update re-evaluates `reserved_seats < capacity`
  on the current row after the wait, so it succeeds whenever a seat is free and needs no retry. The
  optimistic check also requires `reserved_seats = :observed`, so it fails on any concurrent change
  even with seats left, and the application must roll back, read again and retry (#16). On a busy trip
  that means more failed attempts and retries for the same result.
- Count changed and returned to its original value (ABA): B's check compares only the number. If
  someone booked (0→1) and cancelled (1→0) after B read, B sees 0 = 0 and passes, unaware that
  anything happened. For a pure seat counter this is harmless (0 is still 0), but it is dangerous
  when the decision depends on more than the number, e.g. status or capacity changed and changed back.
- A version column: incremented on every relevant change, it only grows, so the check
  `version = :observed_version` fails after any change in between, including 0→1→0. It detects
  "something changed" reliably; the capacity condition still decides whether a seat is free.

## 5. Isolation levels

The original unsafe read-then-write sequence from task 1 (`unsafe-read.sql`, `unsafe-write.sql`),
same order, only the isolation level changed.

Prediction: under Repeatable Read, B fails with `40001` and gets no seat.

### Repeatable Read

Experiments #19–#20. Prediction confirmed. Both sessions read 0 from their snapshot. A wrote and
committed. B's UPDATE of the same row failed with `40001: could not serialize access due to concurrent
update`: the row had been changed and committed after B's snapshot, so PostgreSQL refused to overwrite
it instead of losing A's update (compare #1). B rolled back and started a new transaction; the new read
saw `reserved_seats=1, can_book=f`, so no booking was written. Final: 1 booking, 1 seat.

At Repeatable Read the transaction takes one snapshot at its first statement and every read sees it,
so the same row read twice gives the same answer. The purpose is consistency, not performance.

### Serializable

Experiments #21–#22. Same result and same `40001` error. The retry of the whole transaction read again,
found the trip full and wrote nothing.

### Answers

- Why retrying only the failed statement with the old value is insufficient: the decision was made from
  the read (`seen_reserved = 0`). Repeating the UPDATE would write the stale `0 + 1` again. The read and
  the decision must be repeated too, so the whole transaction is retried from `BEGIN`; in #20 the new
  read showed the trip was full and correctly booked nothing.
- Why the two tests look alike although the guarantees differ: our test has both transactions write
  the same single row, which Repeatable Read already detects. The difference shows when two
  transactions read overlapping data but write different rows: write skew, which the PostgreSQL
  documentation lists as possible under Repeatable Read. Example: a rule "max one booking per passenger
  per day". A and B both check that the passenger has none and each insert a new row. No row was written
  by both, so Repeatable Read detects nothing and the rule is broken; Serializable would abort one of
  them. Protecting this shared counter does not prove that multi-row business rules are safe.

### Handling 40001

- What is repeated: the whole transaction from `BEGIN`: read, decision and writes. Never only the
  failed statement.
- Attempts: a small limit, e.g. 3, with a short random delay between attempts.
- If attempts keep failing: the passenger sees "The booking could not be completed right now, please
  try again" and is not charged.
- Same handling at both levels; Serializable can raise `40001` in more situations (also on read/write
  dependencies, not only same-row updates), so expect more retries there.
- External payment inside the retry loop? No. A rollback only undoes database changes; a payment
  already started cannot be undone by PostgreSQL. Every retry could charge the passenger again, i.e.
  paying twice for the same seat. Pay outside the loop (reserve, pay, confirm), or send an idempotency
  key to the payment provider so a repeated request is recognised.

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
