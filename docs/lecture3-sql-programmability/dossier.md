# Dossier for tasks in SQL Programability
This lecture is about SQL Programability, where we learn about Stored procedures, Views, Functions and DDL/DML Triggers, by implementing them in the fictive Mobility Ticketing System database.

## Tasks

1. ### Run the reference revenue query and verify its result from the base tables.
    - Run the query from database/queries/base_revenue.sql
    Result:
    
    | id | user_id | ticket_id | external_payment_reference | amount | currency | status | created_utc |
    |---|---|---|---|---|---|---|---|
    | PAYMENT-1 | USER-1 | TICKET-1 | gateway-capture-0001 | 36 | DKK | Captured | 2026-04-29 09:40:00.000 +0200 |
    | PAYMENT-2 | USER-2 | TICKET-2 | gateway-capture-0002 | 36 | DKK | Captured | 2026-04-29 10:40:00.000 +0200 |

2. ### Wrap the read logic in a SQL function.
    - Run the query from database/migrations/020_reporting_function.sql, and verify that it works.
    - Operator: OP-METRO
    ```sql
    select * from captured_revenue_for_day('OP-METRO', '2026-04-29')
    ```
    Output: 
    | captured_amount | captured_payments |
    |----|---|
    | 36 | 1 |

    - Operator: OP-BUS
    ```sql
    select * from captured_revenue_for_day('OP-BUS', '2026-04-29')
    ```
    Output: 
    | captured_amount | captured_payments |
    |----|---|
    | 36 | 1 |

3. ### Create a materialized view and observe when it becomes stale.
    - Run the sql script in database/migrations/022_daily_captured_revenue.sql
    output: Created an empty View, and a big error message 
    > SQL Error [55000]: ERROR: materialized view "daily_captured_revenue" has not been populated
    >Hint: Use the REFRESH MATERIALIZED VIEW command.
    - Create populated view with sql query:
    ```sql
    refresh materialized view daily_captured_revenue;
    ```
    
    Output:
    | operator_id | revenue_date | captured_amount | captured_payments |
    |---|---|---|---|
    | OP-BUS | 2026-04-29 | 36 | 1 |
    | OP-METRO | 2026-04-29 | 36 | 1 |

    - Run the queries from database/postgres/experiments/reporting_cases.sql, and refresh the daily_captured_revenue view.
    Output:
    ```sql 
    SELECT * FROM daily_captured_revenue;
    ```
    | operator_id | revenue_date | captured_amount | captured_payments |
    |---|---|---|---|
    | OP-BUS | 2026-04-29 | 36 | 1 |
    | OP-METRO | 2026-04-29 | 36 | 1 |

    >Here we receive the outdated view, where we need to refresh the materialized view, to get a new "snapshot" of the table.

    - Refresh view
    ```sql
    refresh materialized view daily_captured_revenue;
    SELECT * FROM daily_captured_revenue;
    ```
    Output: 

    | operator_id | revenue_date | captured_amount | captured_payments |
    |---|---|---|---|
    | OP-BUS | 2026-04-29 | 36 | 1 |
    | OP-METRO | 2026-04-29 | 72 | 2 |
4. ### Create the supplied trigger-maintained summary.
    - Run the query from database/postgres/migrations/021_daily_revenue_trigger.sql
        This creates a new table in the database:
        >daily_revenue_by_operator
        
        And  triggers for the table to insert data for each new payment.
        The problem is, that it doesn't trigger when there are an update to - or deletion of - an existing payment.
    - Run the queries from database/postgres/experiments/reporting_cases.sql

        Terminal output:
            SET
    | case_name | operator_id | revenue_date | captured_amount | captured_payments
    -----------+-------------+--------------+-----------------+-------------------
     baseline  | OP-BUS      | 2026-04-29   |           36.00 |                 1
     baseline  | OP-METRO    | 2026-04-29   |           36.00 |                 1
    (2 rows)

    INSERT 0 1   -- case 1: captured payment insert
    INSERT 0 1   -- case 2: failed payment insert
    UPDATE 1     -- case 3: Failed -> Captured correction
    UPDATE 1     -- case 4: Captured -> Refunded correction
    DELETE 1     -- case 5: delete test data
    INSERT 0 1   -- case 6: duplicate delivery

    
    - Result of `select * from daily_revenue_by_operator;`:

    | operator_id | revenue_date | captured_amount | captured_payments |
    |---|---|---|---|
    | OP-METRO | 2026-04-29 | 72 | 2 |

    - **Observations**

        - Kun 72/2 for OP-METRO kommer fra de to `INSERT`-cases (case 1 og case 6), fordi trigger'en *kun* reagerer på `INSERT`. De to `UPDATE`-cases (case 3 og 4) og `DELETE`-casen (case 5) rammer slet ikke tabellen, selvom de ændrer den underliggende sandhed i `payments`.
        - Case 6 (duplicate delivery, samme `external_payment_reference` som PAYMENT-1) bliver talt med som en helt ny betaling — der er ingen dedup-logik, så beløbet lægges oveni i stedet for at blive genkendt som en gentagelse.
        - **OP-BUS mangler helt fra tabellen** — ikke fordi noget gik galt, men fordi OP-BUS' eneste `Captured`-betaling (`PAYMENT-2`) blev indsat via seed-scriptet, *før* trigger'en overhovedet eksisterede. En `AFTER INSERT`-trigger kan kun reagere på inserts, der sker efter den er oprettet — den kan ikke "se tilbage" på eksisterende data. Det er faktisk det, migrationsfilens TODO-kommentar kalder "initial backfill", og jeres resultat viser det helt konkret.

5. ### Test all four approaches against:
   - a captured payment insert;
   - a failed payment insert;
   - a status correction from `Failed` to `Captured`;
   - a correction from `Captured` to `Refunded`;
   - deletion or replacement of test data;
   - duplicate delivery of the same external payment reference.

   | Approach | Queried | OP-BUS (revenue/number) | OP-METRO (revenue/number) |
   |----|----|----|----|
   | Base query / Function | After all 6 cases | 36/1 | 72/2 |
   | Materialized view | After cases, before refresh | 36/1 | 36/1 |
   | Materialized view | After refresh | 36/1 | 72/2 |
   | Trigger-table | After all 6 cases (no refresh-concept) | (No rows) | 72/2 |
   1. **Materialized view vs base query/function, after cases**: The view says 36/1, the two others says 72/2 - the same instance, but different answers, for the different approaches.
   2. **Trigger-table shows 72/2 for OP-METRO - but of the wrong reasons**: It didn't register the two UPDATE-corrections, nor the DELETE. Furthermore it counts duplicate-deliverances as new revenue - that it's result are the same as the three others are an coincidence, but not a proff of correctness, and OP-BUS are completely missing, as shown in Task 4.

## Side-effect trace: one `INSERT INTO payments`

**Case A — valid captured payment (referencing an existing ticket):**

```
Insert on payments (actual time=0.250..0.250 rows=0 loops=1)
Planning Time: 0.094 ms
Trigger payments_daily_revenue_after_insert: time=5.211 calls=1
Execution Time: 5.475 ms
```

- Constraints/references checked: none on `payments` itself (no FK on `ticket_id`/`user_id`); only the primary key (`id`).
- Trigger execution: `payments_daily_revenue_after_insert` fires once, after the insert.
- Summary-table writes: one upsert into `daily_revenue_by_operator` (insert or accumulate).
- Rows touched: 1 row read via join across `tickets` → `trips` → `routes`; 1 row written/updated in `daily_revenue_by_operator`.
- Write cost: the trigger accounts for ~95% of total execution time (5.211ms of 5.475ms) — the plain insert alone costs 0.250ms.
- Commit/rollback: succeeds; both writes commit together.
- Report currency: base query/function reflect it on next call; materialized view not until next `refresh`; `daily_revenue_by_operator` reflects it immediately on commit.

**Case B — captured payment referencing a non-existent ticket (`ticket_id = 'DOES-NOT-EXIST'`):**

```
SQL Error [23502]: ERROR: null value in column "operator_id" of relation "daily_revenue_by_operator" violates not-null constraint
  Detail: Failing row contains (null, 2026-04-29, 36, 1).
  Where: SQL statement "insert into daily_revenue_by_operator (
        operator_id, revenue_date, captured_amount, captured_payments
    ) values (
        payment_operator_id, new.created_utc::date, new.amount, 1
    )
    on conflict (operator_id, revenue_date)
    do update set
        captured_amount = daily_revenue_by_operator.captured_amount + excluded.captured_amount,
        captured_payments = daily_revenue_by_operator.captured_payments + 1"
PL/pgSQL function add_inserted_payment_to_daily_revenue() line 16 at SQL statement
```

- The failure originates inside `add_inserted_payment_to_daily_revenue()` (line 16), at the `insert into daily_revenue_by_operator` statement — not in `payments` itself.
- `payment_operator_id` is `NULL` because the trigger's earlier `select ... into payment_operator_id` found no matching row across `tickets` → `trips` → `routes` for the given `ticket_id`.
- Because the trigger executes inside the same transaction as the originating `INSERT INTO payments`, this constraint violation aborts the whole transaction — the payment row is never persisted, despite `payments` having no constraint that would itself reject it.
- What the application observes: a raw `23502` (`not_null_violation`) Postgres error surfacing from a table (`daily_revenue_by_operator`) the caller never referenced directly — a leaky abstraction caused entirely by the trigger's dependency on referential data that `payments` doesn't itself enforce.

6. Produce a responsibility matrix comparing correctness, freshness, write cost, read cost, hidden side effects, rebuildability, and operational complexity.
## Responsibility matrix

| Dimension | Base query | Function (020) | Materialized view (022) | Trigger table (021) |
|---|---|---|---|---|
| **Correctness** | Always correct — recomputes from source on every call | Same as base query (identical logic, just wrapped) | Correct only immediately after `refresh`; otherwise stale/wrong | Incomplete — misses UPDATE/DELETE entirely, double-counts duplicates, and is missing pre-existing data (OP-BUS) |
| **Freshness** | Always live | Always live | Only as fresh as the last `refresh` | Immediate for plain inserts — but silently wrong for corrections and deletions |
| **Write cost** | None | None | None at write time; cost shifts to `refresh` (full recompute) | Highest — trigger adds ~5.2ms per insert (~95% of total execution time, from our trace) |
| **Read cost** | Highest — full 3-way join recomputed every call | Same as base query | Low — simple lookup on precomputed rows | Lowest — simple lookup on a tiny table |
| **Hidden side effects** | None | None | Silent staleness — no visible signal that data may be outdated | Most severe — a valid payment insert can fail entirely due to an unrelated table's constraint (our Case B); corrections/deletes diverge silently, no error |
| **Rebuildability** | N/A — no stored state | N/A — no stored state | Trivial and complete — `refresh` always recomputes fully from truth | Worst — no backfill path for pre-existing data or for the current gaps; needs manual reconciliation |
| **Operational complexity** | Lowest — one ad-hoc query | Low — one function definition, centralizes logic | Medium — needs a refresh strategy/schedule | Highest — table + function + trigger, needs extending to be trustworthy, plus a backfill process |

**Cross-cutting note:** duplicate delivery (same `external_payment_reference`) inflates revenue in **all four** approaches equally — none of them deduplicate on that column. It's not a differentiator between the approaches; it's a shared data-quality gap, better suited to the issue register than to any single cell above.

7. Recommend one approach for the current case. A hybrid answer is allowed, but each stored copy must have a clear authority and rebuild path.
    - Our recomended approach would be, to have a scheduled task to create the snapshot-view each night at 00:00:00, and porting this result over to an accounting database 