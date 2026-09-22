# What we built, and how the progression went

This document is the map for the four Databases for Developers lectures behind
this Compulsory Assignment 1 submission. It explains how the database evolved
week by week and where each piece of evidence lives.

## The two starting points

Lecture 1 was worked as a self-contained exercise: its own baseline schema
(`docs/lecture1-intro-to-databases/sql/001_relational_baseline.sql`) and seed
data, with our own route-stop primary-key choice and route data. The original
workload map, assumptions and first schema draft that this was based on live
in `docs/lecture1-intro-to-databases/original-design/` (carried over from our
early exploration in the `Mobility-tracker` repository, before the official
lecture starters were released).

From Lecture 2 onward, the course supplied one shared reference baseline
(`database/postgres/init/001_relational_baseline.sql` and `002_seed.sql`) that
is not the same file as Lecture 1's own version — it is byte-identical across
the Lecture 2, 3 and 4 starter repositories. Everything from Lecture 2 onward
in this repository builds on that shared baseline, not on our Lecture 1
answer.

## The migration chain (Lecture 2 → 4)

| Stage | File | Origin | Status |
|---|---|---|---|
| Baseline | `database/postgres/init/001_relational_baseline.sql`, `002_seed.sql` | Course-supplied reference | Given |
| Ticketing draft | `database/postgres/init/010_ticketing_draft.sql`, `011_ticketing_seed.sql` | Course-supplied reference | Given |
| Integrity constraints | `database/postgres/migrations/011_ticketing_integrity.sql` | **Our own Lecture 2 work** | Done — see `docs/lecture2-sql-operations/Mobility_Integrity_Uge36.md` |
| Reporting function | `database/postgres/migrations/020_reporting_function.sql` | Lecture 3 | Applied, analysed in `docs/lecture3-sql-programmability/dossier.md` |
| Revenue trigger | `database/postgres/migrations/021_daily_revenue_trigger.sql` | Lecture 3 | Applied, analysed |
| Materialised view | `database/postgres/migrations/022_daily_captured_revenue.sql` | Lecture 3 | Applied, analysed |
| Migration fixture (ticket at 65 DKK) | `database/postgres/init/013_migration_fixture.sql` | Course-supplied reference | Given |
| Product identity expansion | `database/postgres/migrations/030_expand_product_identity.sql` | **Our own Lecture 4 work** | TODO |
| Backfill | `database/postgres/migrations/031_backfill_ticket_product.sql` | Lecture 4 | TODO |
| Require new reference | `database/postgres/migrations/032_require_ticket_product.sql` | Lecture 4 | TODO |

One deliberate decision: the Lecture 4 starter repository ships its own
"completed integrity" reference state (`init/012_completed_integrity.sql`),
which is the course's model answer, not ours, and adds a few constraints our
own Lecture 2 work left open (status-value and currency-format checks). We
build Lecture 4 on top of our own `011_ticketing_integrity.sql` instead, per
the Lecture 4 brief's instruction to "keep your earlier constraints" — so
`init/012_completed_integrity.sql` is intentionally not included here.

## Where the supporting material lives

- `docs/lecture1-intro-to-databases/` — workload map, relational model,
  schema, seed data and the three route/timetable queries, plus the earlier
  design material in `original-design/`.
- `docs/lecture2-sql-operations/` — integrity map, constraint migration and
  write-test evidence.
- `docs/lecture3-sql-programmability/` — reporting query/function, trigger
  and materialised-view experiments, and the responsibility decision.
- `docs/lecture4-schema-migration/` — product-identity migration lab; the
  evidence in `evidence/` is still to be filled in once the migration work
  is done.

## What is still open

Lecture 4 is the outstanding work: the three migrations above, the old/new
reader and writer scripts under
`database/postgres/experiments/lecture04/`, and the compatibility and
price-preservation evidence the lab asks for. See
`docs/lecture4-schema-migration/lab.md` for the full task list.
