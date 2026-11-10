# Transactions: The last seat

Reset the fixture between runs, after both sessions have ended their transactions.
Order notation: `A:read → B:read → A:write → A:commit → B:write → B:commit`.
`(waits)` marks a statement that blocked until the other session ended its transaction.

## Experiments

| # | Task / variant | Isolation | Trip | Order (A/B) | Who waits, on what | Error / SQLSTATE | Final bookings | reserved_seats |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 1 / unsafe read-then-write | Read Committed | T-LAST | A:read → B:read → A:write → A:commit → B:write → B:commit | Nobody (A committed before B wrote) | None | 2 (unsafe-A, unsafe-B) | 1 (`counts_agree = f`) |
| 2 | 1 / `variants/01-unsafe-increment.sql` (`reserved_seats + 1`) | Read Committed | T-LAST | A:read → B:read → A:write → A:commit → B:write (fails) → B:rollback | Nobody (A committed before B wrote) | B UPDATE: `23514` check violation `trips_check`, failing row `(T-LAST, Scheduled, 1, 2)` | 1 (inc-A) | 1 (`counts_agree = t`) |

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

### Prediction

### Result

### Answers

## 3. Explicit locks

### Row lock (`SELECT ... FOR UPDATE`)

### Table lock (`LOCK TABLE ... IN EXCLUSIVE MODE`)

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
