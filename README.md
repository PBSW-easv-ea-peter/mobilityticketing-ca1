# MobilityTicketing — Compulsory Assignment 1

This is the consolidated repository for Compulsory Assignment 1 in Databases
for Developers. It brings together the work from all four weekly labs
(previously spread across four separate starter repositories) into one
database and one review guide. See `docs/PROGRESSION.md` for how the pieces
fit together and where each originally came from.

## Setup and reset

Requirements: Docker Desktop with Compose.

```bash
docker compose up -d
docker compose ps
```

The database is available at `localhost:5432` (database, user and password:
`mobility`). The scripts in `database/postgres/init/` run only against an
empty data directory, so replay them with:

```bash
docker compose down -v
docker compose up -d
```

Migrations in `database/postgres/migrations/` are applied in numeric order,
for example:

```bash
docker compose exec -T postgres psql -U mobility -d mobility < database/postgres/migrations/011_ticketing_integrity.sql
```

## Repository layout

- `database/postgres/init/` — shared starting schema and seed data.
- `database/postgres/migrations/` — the constraint, reporting and
  product-identity migrations, in the order they were applied (see
  `docs/PROGRESSION.md` for the full chain and status of each).
- `database/postgres/queries/` and `database/postgres/experiments/` — the
  released reporting query and the test/experiment scripts for each lab.
- `docs/lecture1-intro-to-databases/` … `docs/lecture4-schema-migration/` —
  each week's lab brief, dossier and evidence.

---

# Compulsory Assignment 1 review guide

Submitted commit: `<TODO: fill in after the final commit>`
Setup and reset instructions: see "Setup and reset" above

## Where to find the work

Lecture 1: model, workload map and queries — [`docs/lecture1-intro-to-databases/`](docs/lecture1-intro-to-databases/), original workload map and assumptions in [`original-design/`](docs/lecture1-intro-to-databases/original-design/), queries in [`sql/003_queries.sql`](docs/lecture1-intro-to-databases/sql/003_queries.sql).

Lecture 2: constraints and tests — migration in [`database/postgres/migrations/011_ticketing_integrity.sql`](database/postgres/migrations/011_ticketing_integrity.sql), integrity map and test evidence in [`docs/lecture2-sql-operations/Mobility_Integrity_Uge36.md`](docs/lecture2-sql-operations/Mobility_Integrity_Uge36.md).

Lecture 3: reporting experiment and comparison — migrations in [`database/postgres/migrations/`](database/postgres/migrations/) (020–022), experiment cases in [`database/postgres/experiments/reporting_cases.sql`](database/postgres/experiments/reporting_cases.sql), analysis and responsibility matrix in [`docs/lecture3-sql-programmability/dossier.md`](docs/lecture3-sql-programmability/dossier.md).

Lecture 4: migration stages and verification — migrations in [`database/postgres/migrations/`](database/postgres/migrations/) (030–032), experiment scripts in [`database/postgres/experiments/lecture04/`](database/postgres/experiments/lecture04/), evidence in [`docs/lecture4-schema-migration/evidence/`](docs/lecture4-schema-migration/evidence/).

*`<TODO: all four links above should point at the exact submitted commit once the migration work in Lecture 4 is finished>`*

## Two decisions worth discussing

*`<TODO — pick two. What did we choose? What was the alternative? Why does our choice fit MobilityTicketing? Which file or result supports it?`*
> Mit (Peter) Forslag til besvarelse:
- **FK design on `validations`.** We thought about three ways to link a validation to a ticket:
  - `validations.ticket_id -> tickets.id`,
  - `validations.ticket_code -> tickets.ticket_code`,
  - or a composite FK on both.

  We chose the composite FK `(ticket_id, ticket_code) -> tickets(id, ticket_code)`, backed by a `UNIQUE (id, ticket_code)` on `tickets`, with the reasoning: a simple FK on `ticket_id` alone wouldn't catch it if a validation mistakenly got a `ticket_code` belonging to a different ticket than the one `ticket_id` points to — the two fields could diverge without the database objecting. The composite FK makes that an impossible state, instead of just an assumption.
  See [`docs/lecture2-sql-operations/Mobility_Integrity_Uge36.md`](docs/lecture2-sql-operations/Mobility_Integrity_Uge36.md) (invariant #6).
- **Responsibility placement for daily revenue.** We looked at four ways to answer "what is the captured revenue for today":
  - A direct query.
  - A SQL function (stored procedure).
  - A materialized view.
  - A trigger-maintained summary table.

  We recommended a hybrid (base query/function as authority, nightly refresh of a snapshot for reporting), and rejected the trigger table as a source of truth. The trigger only reacts to `INSERT`. It missed two status corrections entirely (`Failed → Captured` and `Captured → Refunded`), it counted a duplicate delivery of the same `external_payment_reference` as new revenue instead of a repeat, and OP-BUS' only payment was missing from the table completely — not because anything went wrong, but because it was inserted via the seed script before the trigger existed at all (an `AFTER INSERT` trigger can't see pre-existing data).
  See [`docs/lecture3-sql-programmability/dossier.md`](docs/lecture3-sql-programmability/dossier.md), the responsibility matrix and side-effect trace.

*`Candidates from the work so far: the route_stops primary-key choice (route_id, stop_sequence) in Lecture 1 vs. (route_id, stop_id) — see the dossier's candidate-key table; or why the Lecture 3 responsibility matrix ruled out the trigger-maintained table as the source of truth despite its low read cost.>`*

## One limitation or open question

*`<TODO — what does our implementation not guarantee, or what are we still unsure about? Point to the relevant evidence. State what we would check next.`*

*`Candidate: the reserved_seats race condition (Issue 1 in the Lecture 2 integrity map) — the CHECK constraint only guarantees single-row consistency, not protection against two concurrent purchases both reading and incrementing reserved_seats.>`*
