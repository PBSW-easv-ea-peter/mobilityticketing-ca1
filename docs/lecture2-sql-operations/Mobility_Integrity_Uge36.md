# MobilityTicketing — Integrity Map (Uge 36)

This document collects the invariants implemented in `database/postgres/migrations/011_ticketing_integrity.sql`, the issues identified while testing them, and the open modelling decisions that remain.

## Integrity map

| Invariant | Affected tables and columns | Current protection | Missing protection or limitation | Expected failure behaviour | Evidence |
|---|---|---|---|---|---|
| 1. Capacity cannot be negative | trips.capacity | NOT NULL; CHECK (capacity >= 0) via constraint trips_capacity_non_negative | No upper bound tied to the vehicle's actual seat count | ERROR: violates check constraint "trips_capacity_non_negative" (SQLSTATE 23514) | Docker test: capacity = -1 fails 23514; capacity = 20 succeeds (UPDATE 1) |
| 2. Reserved seats cannot be negative or greater than capacity | trips.reserved_seats, trips.capacity | NOT NULL; CHECK (reserved_seats BETWEEN 0 AND capacity) via trips_reserved_seats_valid. Combines a direct rule (>= 0) with a cross-column rule (<= capacity) | Single-row check only; does not protect against two concurrent purchase transactions both reading and incrementing reserved_seats (see Issue 1) | ERROR: violates check constraint "trips_reserved_seats_valid" (SQLSTATE 23514) | Docker test: capacity = 20, reserved_seats = capacity + 1 (21) fails 23514; reserved_seats = 15 succeeds (UPDATE 1) |
| 3. Ticket price and payment amount cannot be negative | products.price, tickets.price, payments.amount | CHECK (price >= 0) on products and tickets; CHECK (amount >= 0) on payments, via products_price_non_negative, tickets_price_non_negative, payments_amount_non_negative. Numeric type (not integer) to allow decimals | No note yet on whether product and ticket price should also match at purchase time (cross-table, not just non-negative) | ERROR: violates check constraint (SQLSTATE 23514) | Docker test — payments.amount = -36 fails (payments_amount_non_negative), amount = 57 succeeds (INSERT 0 1); products.price = -1 fails (products_price_non_negative), price = 100 succeeds; tickets.price = -36 fails (tickets_price_non_negative), price = 36 succeeds |
| 4. Tickets must reference existing users, trips, and products | tickets.user_id, tickets.trip_id, tickets.product_code | FOREIGN KEY constraints tickets_user_fk -> users(id), tickets_trip_fk -> trips(id), tickets_product_fk -> products(code) | Does not capture which stop the passenger boards at (see Issue 2); does not enforce that the product type matches how the ticket is validated (see Issue 3) | ERROR: violates foreign key constraint (SQLSTATE 23503) | Docker test — unknown trip_id fails tickets_trip_fk; unknown product_code fails tickets_product_fk; unknown user_id fails tickets_user_fk, all SQLSTATE 23503; a ticket with valid user, trip and product succeeds (INSERT 0 1) |
| 5. Payments must reference existing tickets | payments.ticket_id | FOREIGN KEY constraint payments_ticket_fk -> tickets(id) | None beyond the general FK limitation (does not itself prevent duplicate payments for one ticket) | ERROR: violates foreign key constraint "payments_ticket_fk" (SQLSTATE 23503) | Docker test: ticket_id = 'NO-SUCH-TICKET' fails 23503; ticket_id = 'T-VALID-1' succeeds (INSERT 0 1) |
| 6. Validations must reference existing tickets | validations.ticket_id, validations.ticket_code, tickets.id, tickets.ticket_code | validations stores both ticket_id and ticket_code separately, so a plain FK on ticket_id alone would not stop the two from belonging to different tickets. Solved with a composite FK: validations_ticket_fk (ticket_id, ticket_code) -> tickets(id, ticket_code), backed by UNIQUE (id, ticket_code) on tickets | Evidence not yet captured | ERROR: violates foreign key constraint (SQLSTATE 23503) | Pending — mismatched ticket_id/ticket_code test drafted (test 10 in the evidence file), not yet run and confirmed |
| 7. Ticket codes must identify tickets unambiguously | tickets.ticket_code | UNIQUE constraint tickets_ticket_code_unique. Makes ticket_code a second candidate key alongside the primary key id (Watt terminology) | None — fully covered by the unique constraint | ERROR: violates unique constraint "tickets_ticket_code_unique" (SQLSTATE 23505) | Docker test: inserting a duplicate ticket_code (copied from an existing ticket) fails 23505 |
| 8. Ticket validity cannot end before it begins | tickets.valid_from_utc, tickets.valid_to_utc | CHECK (valid_to_utc > valid_from_utc) via tickets_validity_window_valid. Cross-column, same category as reserved_seats <= capacity | Strict inequality (> rather than >=) is an assumption; not yet re-checked against every product type | ERROR: violates check constraint "tickets_validity_window_valid" (SQLSTATE 23514) | Docker test: valid_from_utc = 09:00, valid_to_utc = 08:00 (reversed) fails 23514; a normal forward window succeeds (INSERT 0 1) |
| 9. Status values must come from an accepted set | tickets.status (possibly trips.status) | None yet. Draft list proposed: trips — planlagt, igang, aflyst, afbrudt, gennemført; payments — oprettet, igang, betalt, mislykket, refunderet | The accepted list of status values has not been agreed by the domain. Once agreed, becomes a direct constraint | N/A — not yet implemented | Pending |
| 10. External payment references must not accidentally represent the same payment more than once | payments.external_payment_reference | UNIQUE constraint payments_external_reference_unique | Unique globally rather than per payment provider; revisit if a second provider is ever introduced alongside Stripe | ERROR: violates unique constraint "payments_external_reference_unique" (SQLSTATE 23505) | Docker test: inserting a payment reusing an already-used external_payment_reference fails 23505 |
| 11. Currency must be present and consistently represented | products.currency, tickets.currency, payments.currency | None yet | "Present" (NOT NULL) is straightforward. "Consistently represented" needs a decision: single-currency (CHECK (currency = 'DKK')) vs. a fixed ISO 4217 list (CHECK (currency IN (...))) | N/A — not yet implemented | Pending |

## Note on partial execution

`011_ticketing_integrity.sql` runs as a single transaction (`begin ... commit`). Each invariant is wrapped in its own `do $$ ... end $$` block with an `if not exists` check against `pg_constraint`, so re-running the file after a successful run is safe — every block sees its constraint already in place and does nothing. If a genuinely new statement in the file fails (a typo, a missing column, a real constraint violation), Postgres aborts the whole transaction and rolls back every block in the file, not just the failing one (seen earlier when `tickets_capacity_non_negative` already existed: BEGIN / ERROR / ROLLBACK, with nothing committed). The idempotency check protects against re-running the same already-applied migration; it does not make a partially-failing run partially succeed.

## Issue register

### Issue 1 — reserved_seats race condition

**Evidence:** trips_reserved_seats_valid is a single-row CHECK constraint

**Problem:** The constraint only guarantees consistency within one row at commit time. It does not stop two concurrent purchase transactions from both reading reserved_seats = capacity - 1 and both committing an increment.

**Consequence:** The ticket purchase workload, described in the design brief as correctness-critical, could in theory oversell a trip under high concurrency.

**Specific improvement:** Use `SELECT ... FOR UPDATE` on the trip row during purchase, or an atomic `UPDATE trips SET reserved_seats = reserved_seats + 1 WHERE id = ... AND reserved_seats < capacity`, checking that exactly one row was updated.

**Open question:** Should the race condition be handled at the database level (locking) or in the application layer?

### Issue 2 — boarding stop not captured

**Evidence:** tickets references trip_id only, not a specific stop on the route

**Problem:** The system can tell which trip a ticket belongs to, but not where on the route the passenger boards or alights.

**Consequence:** Imprecise capacity management per route segment, and less precise validation.

**Specific improvement:** Consider a reference to route_stops (boarding stop) on ticket or validation.

**Open question:** Is this needed for the current phase of the system, or only once validation logic is designed?

### Issue 3 — product type not enforced against validation rules

**Evidence:** tickets.product_code references products, but no constraint ties product type to how a ticket may be validated

**Problem:** The system does not distinguish, at the constraint level, between e.g. a single trip and a day pass when a ticket is validated.

**Consequence:** A ticket could potentially be validated in a way that does not match the purchased product.

**Specific improvement:** Consider whether product type should drive validation logic once that workload is designed.

**Open question:** Does product-type validation belong in this migration, or in the later validation workload?

## State-transition trace

Not yet completed. The migration so far covers structural and referential integrity (capacity, reserved seats, price/amount, foreign keys, ticket-code uniqueness, validity window). The allowed state transitions for ticket purchase and ticket validation depend on the still-open "status values" decision above and have not been defined yet.

**Ticket purchase:** 1. 2. 3. — pending status-value decision.

**Ticket validation:** 1. 2. 3. — pending status-value decision.

## Appendix — constraints implemented so far

```sql
do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'trips_capacity_non_negative') then
        alter table trips
            alter column capacity set not null,
            alter column reserved_seats set not null,
            add constraint trips_capacity_non_negative
                check (capacity >= 0),
            add constraint trips_reserved_seats_valid
                check (reserved_seats between 0 and capacity);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'tickets_user_fk') then
        alter table tickets
            add constraint tickets_user_fk
                foreign key (user_id) references users(id);
    end if;

    if not exists (select 1 from pg_constraint where conname = 'tickets_trip_fk') then
        alter table tickets
            add constraint tickets_trip_fk
                foreign key (trip_id) references trips(id);
    end if;

    if not exists (select 1 from pg_constraint where conname = 'tickets_product_fk') then
        alter table tickets
            add constraint tickets_product_fk
                foreign key (product_code) references products(code);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'payments_amount_non_negative') then
        alter table payments
            add constraint payments_amount_non_negative
                check (amount >= 0);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'products_price_non_negative') then
        alter table products
            add constraint products_price_non_negative
                check (price >= 0);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'tickets_price_non_negative') then
        alter table tickets
            add constraint tickets_price_non_negative
                check (price >= 0);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'payments_ticket_fk') then
        alter table payments
            add constraint payments_ticket_fk
                foreign key (ticket_id) references tickets(id);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'tickets_ticket_code_unique') then
        alter table tickets
            add constraint tickets_ticket_code_unique
                unique (ticket_code);
    end if;

    if not exists (select 1 from pg_constraint where conname = 'tickets_validity_window_valid') then
        alter table tickets
            add constraint tickets_validity_window_valid
                check (valid_to_utc > valid_from_utc);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'tickets_id_ticket_code_unique') then
        alter table tickets
            add constraint tickets_id_ticket_code_unique
                unique (id, ticket_code);
    end if;

    if not exists (select 1 from pg_constraint where conname = 'validations_ticket_fk') then
        alter table validations
            add constraint validations_ticket_fk
                foreign key (ticket_id, ticket_code) references tickets(id, ticket_code);
    end if;
end $$;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'payments_external_reference_unique') then
        alter table payments
            add constraint payments_external_reference_unique
                unique (external_payment_reference);
    end if;
end $$;
```
