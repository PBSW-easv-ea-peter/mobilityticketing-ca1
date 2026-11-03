# Query optimization notes

Use the `Execution Time` from each of the five measured plans. Sort the five values to find the median. Record the smallest and largest as the range.

| Route | Query / active index | Median ms | Range ms | Scan / filter / sort observation | Top-node shared hit / read buffers | Check passes? |
| --- | --- | --- | --- | --- | --- | --- |
| R-7 | Baseline / primary key only | 9.464 | 9.393-12.037 | Seq Scan, 119970 removed by filter, quicksort (38 rows) | hit=1007 / read=0 | Yes (4/4 PASS) |
| R-7 | Rewrite / primary key only | 7.368 | 7.243-7.746 | Seq Scan, 119970 removed by filter, quicksort (38 rows) | hit=1007 / read=0 | Yes (4/4 PASS) | 
| R-7 | Rewrite / route-only index | 1.845 | 1.756-1.895 | Bitmap Heap Scan via route index, 3299 removed by filter, heap blocks exact=1001, quicksort (38 rows) | hit=1011 / read=0 | Yes (4/4 PASS) | 
| R-7 | Rewrite / chosen index | 0.118 | 0.111-0.193 | Index Scan on (route_id, scheduled_departure_utc, id) WHERE status='Scheduled', no filter, no sort, 20 rows read | hit=23 / read=0 | Yes (4/4 PASS) |
| R-0 | Baseline / primary key only | 18.531 | 18.342-19.367 | Seq Scan, 119017 removed by filter, top-N heapsort (991 rows) | hit=1007 / read=0 | Yes (4/4 PASS) |
| R-0 | Rewrite / primary key only | 7.578 | 7.235-8.264 | Seq Scan, 119017 removed by filter, top-N heapsort (991 rows) | hit=1007 / read=0 | Yes (4/4 PASS) | 
| R-0 | Rewrite / route-only index | 7.507 | 7.308-7.839 | Seq Scan, 119017 removed by filter, top-N heapsort (991 rows) | hit=1007 / read=0 | Yes (4/4 PASS) | 
| R-0 | Rewrite / chosen index | 0.124 | 0.117-0.162 | Index Scan on (route_id, scheduled_departure_utc, id) WHERE status='Scheduled', no filter, no sort, 20 rows read | hit=24 / read=0 | Yes (4/4 PASS) |

## Prediction (before changes)

- Part of the plan to reduce: the Seq Scan. It reads all 1001 pages and removes
  ~119,000 rows by filter to return 38 (R-7) / 991 (R-0). Sort costs ~0.05 ms
  and is not the bottleneck.
- Rewrite alone (no new index): still a Seq Scan with 1001 buffers, but slightly
  faster, because comparing timestamps is cheaper than converting every row
  with AT TIME ZONE ... ::date. Biggest gain expected for R-0, where ~90,000
  rows pass the route filter and reach the date condition.
- Rewrite + suitable index: an index scan instead of a Seq Scan, few or no rows
  removed by filter, far fewer buffers. If the index delivers rows in
  departure order, the Sort node disappears and LIMIT can stop after 20 rows.

## Result after rewrite

- Prediction confirmed: still a Seq Scan reading all 1001 pages, but faster.
  R-7 9.464 → 7.368 ms (~22 %), R-0 18.531 → 7.578 ms (~59 %).
- R-0 gained most: the cheap timestamp condition is now evaluated first, so
  ~90,000 R-0 rows no longer reach an expensive per-row conversion.
- Both routes now take ~7.5 ms. That is the floor for scanning 120,008 rows;
  only an index can avoid reading them.

## Prediction: route-only index

- R-7: index used (likely Bitmap Index Scan + Bitmap Heap Scan). It fetches all
  ~3,337 R-7 trips across three months, so date and status are still filtered
  (~3,300 rows removed instead of ~119,970). Sort remains, because the index is
  not ordered by departure time.
- R-0: index ignored. R-0 is ~75 % of the table, so a Seq Scan is cheaper than
  following 90,000 index pointers. Plan and timing same as the rewrite row.

## Result: route-only index

- R-7: prediction confirmed. Bitmap Index Scan finds 3,337 rows, 3,299 are
  removed by filter, Sort remains. ~4x faster (7.368 → 1.845 ms).
- Surprise: buffers did not drop (1007 → 1011). R-7 rows are spread across all
  1001 pages (Heap Blocks: exact=1001). The time gain comes from evaluating
  3,337 rows instead of 120,008, not from reading fewer pages.
- R-0: prediction confirmed. Index ignored, identical Seq Scan plan. The route
  value matches ~75 % of the table, so the index is not selective enough.
- Size: 832 kB.

## Keep or remove?

- Index sizes: route-only 832 kB; chosen index 4408 kB
- Index to keep: trips_route_sched_dep_id_idx on (route_id, scheduled_departure_utc, id)
  WHERE status = 'Scheduled'
- One result that supports the decision: R-0 went from 7.507 ms (route-only, Seq Scan,
  1007 buffers) to 0.124 ms (Index Scan, 24 buffers). No filter and no Sort node, and
  only 20 rows are read because LIMIT stops the scan early. R-7 and R-0 now cost the same.
- One alternative rejected, and why: the route-only index. R-0 is ~75 % of the table,
  so the planner ignored it (identical Seq Scan). For R-7 it still read all 1001 pages
  (Heap Blocks: exact=1001) and needed a filter and a Sort.
- What changes when a scheduled departure is inserted, rescheduled or cancelled?
  Insert: one extra index entry. Reschedule: scheduled_departure_utc is a key column, so
  the update cannot be HOT. New entries are written in every index (also the primary key).
  Cancel: status is in the index predicate, so it is not HOT either. The row leaves the
  partial index, and the dead entry stays until VACUUM. Cost: the index is 4408 kB,
  larger than the primary key (2640 kB), and adds write I/O, WAL and bloat.
- Your AI prompt:
```
Jeg er ved at optimisere en Read query i Postgress 17, og jeg har brug for en second-opinnion om indekseringen.

Jeg vil gerne have ET index for det nedenstående query.

Som kontekst:

Tabellen lab08.trips holder 3 måneder af scheduled trips - cirka 120.000 rows, og omkring 1001 pages.
Queryen er en passenger departure search - så det skal returnere de første 20 scheduled trips på EN rute for EN UTC-dag, sorteret efter departure time, og dernæst id.
route 'R-0' har omkring 90.000 trips (75% af tabellen), og ca. 990 scheduled trips/day. De andre ruter har ca 3300 trips hver, og f.eks. 'R-7' har omkring 38/day.
Trips er inserted, rescheduled(departure time ændringer) og cancelled (status skifter fra 'scheduled' til 'cancelled')
Tabellen er opbygget således:

create table lab08.trips (
    id bigint primary key,
    route_id text not null,
    scheduled_departure_utc timestamptz not null,
    status text not null check (status in ('Scheduled', 'Cancelled'))
);
, min nuværende query er denne:

select id, route_id, scheduled_departure_utc, status
from lab08.trips
where route_id = :'route_id'
  and status = 'Scheduled'
  and scheduled_departure_utc >= (:'query_day'::date)::timestamp at time zone 'UTC'
  and scheduled_departure_utc <  (:'query_day'::date + 1)::timestamp at time zone 'UTC'
order by scheduled_departure_utc, id
limit :result_limit;
med denne execution plan:
Query: candidate | Route: R-0 | UTC day: 2026-10-23
Warm-up (not recorded)

Measured run 1
                                                                                                                        QUERY PLAN                                                                                                                        
----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------
 Limit  (cost=3424.87..3424.92 rows=20 width=30) (actual time=7.475..7.479 rows=20 loops=1)
   Buffers: shared hit=1007
   ->  Sort  (cost=3424.87..3427.10 rows=891 width=30) (actual time=7.474..7.476 rows=20 loops=1)
         Sort Key: scheduled_departure_utc, id
         Sort Method: top-N heapsort  Memory: 27kB
         Buffers: shared hit=1007
         ->  Seq Scan on trips  (cost=0.00..3401.16 rows=891 width=30) (actual time=0.012..7.350 rows=991 loops=1)
               Filter: ((scheduled_departure_utc >= '2026-10-23 00:00:00+00'::timestamp with time zone) AND (scheduled_departure_utc < '2026-10-24 00:00:00+00'::timestamp with time zone) AND (route_id = 'R-0'::text) AND (status = 'Scheduled'::text))
               Rows Removed by Filter: 119017
               Buffers: shared hit=1001
 Planning:
   Buffers: shared hit=117
 Planning Time: 0.400 ms
 Execution Time: 7.507 ms
(14 rows)

Jeg vil nu gerne have dig til at skrive et CREATE INDEX statement, hvorfor du vælger disse kolonner i denne rækkefølge, hvad du forventer at der sker af ændringer i planen for R-7 og for R-0 (scan type, filtering og sortering)
Sidst men ikke mindst, så skal der være en pris eller downside af indekset
```
- One AI suggestion accepted, changed or rejected, and the check that decided it:
  Accepted the suggested index (same as ours). Rejected its claim that an Index Only Scan
  was possible: the plan shows Index Scan, because status is selected but not stored in
  the index. Its size estimate (~4 MB) was confirmed by pg_relation_size (4408 kB).

## Partitioning

| Read | Plain-table plan | Partitions actually accessed | Same result? |
| --- | --- | --- | --- |
| October count | Index Only Scan on validations_plain_pkey, 198 buffers, 5.54 ms | validations_oct only (pruned); Seq Scan, 261 buffers, 5.38 ms | Yes (40,889) |
| Ticket 100 history | Seq Scan, 119,988 removed by filter, 765 buffers, 5.35 ms | validations_sep, validations_oct, validations_nov (no pruning); 766 buffers, 5.86 ms | Yes (12 rows, 0 differences) |

Would you choose monthly partitions for reporting, ticket support, or monthly retention? Refer to the plans.

Answer: Not for these reads. For the October report, pruning limits the scan to
validations_oct, but the plain table already reads only October via its primary key
(validated_utc, id) and touches fewer buffers (198 vs 261). For ticket support, the
partition key is validated_utc, so a filter on ticket_id cannot be pruned and all three
partitions are scanned. An index on ticket_id would help both layouts. For monthly retention,
I would choose partitions: removing a month is DETACH/DROP PARTITION, a quick metadata change,
instead of a DELETE of ~40,000 rows that creates WAL, dead rows and VACUUM work.
