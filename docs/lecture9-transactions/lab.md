# Implementation lab: The last seat

Two passengers try to book the last seat on the same trip. You will reproduce the double booking, implement several ways to prevent it and compare what happens when requests overlap.

Use the [Lecture 9 starter](https://github.com/patrickstolc/mobilityticketing-lecture-9-starter) and follow its README. It supplies PostgreSQL 17, test data, an unsafe booking sequence and checks. Open two database sessions, A and B.

| Situation | Required result |
|-----------|-----------------|
| Two passengers book the last seat | One committed booking and one reserved seat. |
| A booking transaction rolls back | Neither its booking nor its counter change remains. |
| The same successful request arrives again | Return the original booking ID without taking another seat. |
| An existing request ID is used for a different passenger or trip | Reject it and preserve the original booking. |
| A new request targets a full, cancelled or missing trip | Reject it without changing either table. |

Complete every numbered task. Keep each SQL variant in a separate file under `sql/variants/`, including variants you decide against. Use `sql/book.sql` for your final implementation of `lab09.book`. Keep the supplied tables and constraints. The function runs inside the caller's transaction; do not commit inside it.

Extend `docs/evidence.md` with a row for every experiment. Record the isolation level, the order of statements in A and B, where either session waits, errors including SQLSTATE, and the final bookings and reserved-seat count. Reset the fixture between runs, after both sessions have ended their transactions. The supplied checks do not replace these two-session experiments.

## Before you start

Run shell commands from the repository folder. Open two terminal windows and run this in each to get a PostgreSQL prompt:

```bash
docker compose exec postgres psql -X -U mobility -d mobility_l09 -v ON_ERROR_STOP=1 -v VERBOSITY=verbose
```

Type SQL at those prompts. Run `docker compose` commands in a normal shell. A waiting query will not return a prompt until the other transaction finishes. After a transaction error, run `ROLLBACK;` before trying again.

Keep a version before moving to the next approach. For example, run these shell commands after implementing the conditional-update version:

```bash
cp sql/book.sql sql/variants/02-conditional.sql
# Restore that version later:
cp sql/variants/02-conditional.sql sql/book.sql
docker compose exec -T postgres bash /lab/run.sh load
```

Reset reloads `sql/book.sql`, so restore the version you intend to test before resetting.

---

## 1. Reproduce the double booking

1. Follow the unsafe sequence below at `READ COMMITTED`. Let both sessions read availability before either writes. Finish A, then B.
2. Inspect both tables. Identify the stale value that B writes back. Explain why two committed bookings can coexist with a reserved-seat count of one.
3. Explain why `BEGIN` and `COMMIT` did not prevent this result, and why the counter's `CHECK` constraint accepted it.

Now change the write to `SET reserved_seats = reserved_seats + 1`, without a capacity condition in the `WHERE` clause. Keep the booking insert and update in one transaction. Repeat the race and record the constraint error. Roll back the failed transaction and inspect both tables. What does this constraint protect? Does it prove that the number of booking rows agrees with the counter? What would happen if the booking insert committed separately?

### Run the unsafe sequence

Reset the fixture first. In A, run:

```sql
\set trip_id T-LAST
\set request_id unsafe-A
\set passenger_id passenger-A
BEGIN ISOLATION LEVEL READ COMMITTED;
\i /lab/sql/unsafe-read.sql
```

In B, run the same commands with `unsafe-B` and `passenger-B`. Both sessions must finish the read before either writes. In A, run:

```sql
\i /lab/sql/unsafe-write.sql
COMMIT;
```

Then run those two statements in B. Inspect the result with `\i /lab/sql/inspect.sql`. For the constraint experiment, copy the write script to a new file and change its update there; keep the supplied unsafe scripts unchanged.

---

## 2. Use a conditional update

Implement a version that increments the counter only when the trip is scheduled and has spare capacity. Use `UPDATE ... WHERE ... RETURNING` to determine whether it succeeded. Keep the booking insert and counter change in the same transaction. A zero-row result must not leave a booking behind.

1. Start A at `READ COMMITTED`, book `T-LAST` and leave the transaction open. Start a different booking in B. Predict B's result, then commit A and inspect it.
2. Reset and repeat, rolling back A instead. Inspect both tables after B finishes.
3. Explain which statement makes the capacity decision. Why is reading availability first insufficient? Does a conditional update avoid waiting for another writer? What can a zero-row result tell you, and what can it not distinguish?

### Example: test the update on its own

```sql
BEGIN;
UPDATE lab09.trips
SET reserved_seats = reserved_seats + 1
WHERE id = 'T-LAST'
  AND status = 'Scheduled'
  AND reserved_seats < capacity
RETURNING id, reserved_seats;
ROLLBACK;
```

This fragment rolls back and does not insert a booking. Use it to inspect `RETURNING`, then add the booking insert and failure handling in your function. Inside PL/pgSQL, use `RETURNING ... INTO` and check `FOUND` immediately after the statement.

### Run competing bookings

After loading your function and resetting, run in A:

```sql
BEGIN ISOLATION LEVEL READ COMMITTED;
SELECT lab09.book('request-A', 'passenger-A', 'T-LAST');
```

Leave A open. In B, begin at the same isolation level and call `lab09.book('request-B', 'passenger-B', 'T-LAST')`. While B waits, commit or roll back A as required by the test. When B returns, commit a successful booking or roll back a failed attempt, then inspect both tables.

---

## 3. Compare explicit locks

Implement and run all three versions below at `READ COMMITTED`. Acquire the lock before reading availability, then check the trip, change the counter and insert the booking in the same transaction. Repeat the A-commits and A-rolls-back tests for each version.

| Version | Additional experiment and explanation |
|---------|---------------------------------------|
| Row lock: `SELECT ... FOR UPDATE` on the trip | While A holds the lock on `T-LAST`, let B book `T-OTHER`. Then try the same trip. Which operations wait? Explain how the time between acquiring the lock and committing affects other passengers. |
| Table lock: `LOCK TABLE lab09.trips IN EXCLUSIVE MODE` | Repeat with B booking `T-OTHER`. Compare the result with the row lock. What concurrency do you lose even though the passengers want different trips? |
| Transaction advisory lock: `pg_advisory_xact_lock` | Define a consistent numeric lock key for each fixture trip and acquire it in every booking path. Then deliberately run the unsafe path in B without acquiring that advisory lock. Can the lock protect code that ignores the convention? Distinguish advisory-lock waiting from the row locks still taken by updates. |

Finally, create a deadlock using row locks: A locks `T-LAST`, B locks `T-OTHER`, then each requests the other's row. Record the error and roll back both sessions. Repeat with both sessions acquiring rows in the same order. Explain what the application should retry after a deadlock.

### Example: coordinate A and B

First try a read-only lock demonstration. Run the steps in this order, switching terminals where indicated:

1. In A, run:
   ```sql
   BEGIN;
   SELECT id FROM lab09.trips
   WHERE id = 'T-LAST' FOR UPDATE;
   ```
   Leave A's transaction open.
2. In B, run the same two statements. B should wait. Switch to A while B is waiting.
3. In A, run `ROLLBACK;`. B's query can now finish.
4. In B, run `ROLLBACK;`. Repeat with B selecting `T-OTHER`.

This demonstration changes no rows. For your booking tests, add the capacity check and both writes, then compare A committing with A rolling back.

### Table and advisory lock syntax

Inside a transaction, before reading availability, use one of these approaches:

```sql
-- Table lock:
LOCK TABLE lab09.trips IN EXCLUSIVE MODE;

-- Advisory lock for T-LAST:
SELECT pg_advisory_xact_lock(9009, 1);
```

For advisory locks, use namespace `9009` and trip keys `T-LAST = 1`, `T-TWO = 2`, `T-CANCELLED = 3`, `T-OTHER = 4`. Use the same two-integer form and mapping in every cooperating booking path.

---

## 4. Detect a stale read with an optimistic check

Implement another version that reads the current reserved-seat count, then updates only if the count still equals the value it read and capacity is still available. Use `RETURNING` or the affected-row count to detect failure. Keep the booking insert in the same transaction and undo it when the check fails.

1. Use `T-TWO`. Let A and B both read the same count before either writes. Commit A, then let B attempt its update.
2. If B updates zero rows, roll back its attempt, read again in a new transaction and retry. Is there still a seat? Explain why a failed optimistic check does not necessarily mean that the trip is full.
3. Repeat on `T-LAST`, including a run where A rolls back. Compare waiting and retries with the conditional-update version. Do not assume that an optimistic check means the database takes no locks.

The supplied schema has no version column, so this experiment compares the count. Explain what it would fail to detect if the count changed and later returned to its original value. How would a version column, incremented on every relevant change, alter that check? You do not need to change the schema.

### Example: keep the value read by each session

Inside each session's transaction, run:

```sql
SELECT reserved_seats AS observed
FROM lab09.trips WHERE id = 'T-TWO' \gset
\echo Read reserved_seats = :observed
```

`\gset` saves a psql variable. After both reads, use `reserved_seats = :observed` in your update condition, alongside status and capacity checks. These are psql commands, not PL/pgSQL. Separate read and write statements let you control exactly when A and B proceed.

---

## 5. Compare isolation levels

1. Reset and repeat the original unsafe read-then-write sequence with `REPEATABLE READ` in both sessions. Make both reads happen before A writes. Commit A, then finish B. Save the error and SQLSTATE.
2. Roll back B's failed transaction. Start a new transaction and repeat the availability check before deciding whether to book. Explain why retrying only the failed statement with the old value is insufficient.
3. Reset and run the same experiment with `SERIALIZABLE` in both sessions. Record the result and demonstrate a retry of the whole transaction.
4. Explain why these two tests may look alike even though the isolation levels offer different guarantees. Use the [PostgreSQL isolation documentation](https://www.postgresql.org/docs/17/transaction-iso.html) to identify an anomaly still possible under Repeatable Read. Does protecting this shared counter prove that all multi-row business rules are safe?

For each level, describe the application's handling of `40001`: which reads and writes are repeated, how many attempts you allow, and what the passenger sees if attempts keep failing. Would you perform an external payment inside that retry loop? Explain the consequence of repeating it.

---

## 6. Handle repeated requests

Add request-ID handling to your final `lab09.book` implementation. Keep the unique constraint on `request_id`. Capacity protection and duplicate-request handling must both work.

1. Commit a booking on `T-LAST`, then repeat the same request ID and details. Return the original booking ID although the trip is now full.
2. Use the repeated-request sequence below to send the same request concurrently from both sessions against `T-TWO`. Check for one booking and one reserved seat. Repeat with A rolling back before B finishes.
3. Reuse a successful request ID with a different passenger, then a different trip. Reject both without changing the original booking or either counter.
4. Run the supplied checks, including full, cancelled and missing trips and rollback after the function has returned an ID.

Explain why a unique request ID does not stop two different requests from booking the last seat. Also explain why checking for an existing ID before inserting is insufficient without a database constraint. In this lab, failed attempts leave no stored booking; a returned ID is confirmed only after commit.

### Run the same request concurrently

Reset first. Run this in A, then in B while A remains open:

```sql
BEGIN ISOLATION LEVEL READ COMMITTED;
SELECT lab09.book('repeat-A', 'passenger-A', 'T-TWO');
```

Commit A. Once B's call returns, commit B and compare the returned IDs. Reset and repeat with A rolling back instead. Inspect both tables after each run.

---

## 7. Ask AI to review your implementation

Use an AI assistant after you have completed your own implementations and experiments.

1. Give it the schema, your final function and the commands and output from one two-session test. Ask it to find a missed concurrency or retry case and to propose an exact A/B sequence that would expose it.
2. Save your prompt, the model's name and the relevant response in `docs/evidence.md`.
3. Predict the proposed test's result yourself, then run it. Save the actual output. If the proposed sequence cannot run, explain the problem and correct it before testing.
4. Accept or reject the suggestion using the observed rows, waiting or error. If you change your implementation, rerun the affected tests and the supplied checks. If it suggests no useful case, ask it to challenge one assumption in your comparison and test that claim.
5. Write a short conclusion: what did the AI identify, what did your experiment establish, and what remains untested? Agreement from the model is not a test result.

### Example prompt

> Review this PostgreSQL 17 booking function for concurrency and repeated-request errors. Schema: [schema]. Function: [my function]. Here is one two-session experiment, including statement order and actual output: [trace]. Find one case this test does not cover. Give an exact A/B schedule and predict the final bookings, counter and any SQLSTATE. State the assumption your suggestion tests.

Replace the bracketed parts with your work. Write your own prediction before running the suggested test.
