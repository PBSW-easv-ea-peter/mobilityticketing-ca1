# Query optimization notes

Use the `Execution Time` from each of the five measured plans. Sort the five values to find the median. Record the smallest and largest as the range.

| Route | Query / active index | Median ms | Range ms | Scan / filter / sort observation | Top-node shared hit / read buffers | Check passes? |
| --- | --- | --- | --- | --- | --- | --- |
| R-7 | Baseline / primary key only | 9.464 | 9.393-12.037 | Seq Scan, 119970 removed by filter, quicksort (38 rows) | hit=1007 / read=0 | Yes(4/4 PASS) |
| R-7 | Rewrite / primary key only |  | | | | |
| R-7 | Rewrite / route-only index | | | | | |
| R-7 | Rewrite / chosen index | | | | | |
| R-0 | Baseline / primary key only | 18.531 | 18.342-19.367 | Seq Scan, 119017 removed by filter, top-N heapsort (991 rows) | hit=1007 / read=0 | Yes (4/4 PASS) |
| R-0 | Rewrite / primary key only | | | | | |
| R-0 | Rewrite / route-only index | | | | | |
| R-0 | Rewrite / chosen index | | | | | |

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

## Keep or remove?

- Index sizes: route-only ___; chosen index ___
- Index to keep:
- One result that supports the decision:
- One alternative rejected, and why:
- What changes when a scheduled departure is inserted, rescheduled or cancelled?
- Your AI prompt:
- One AI suggestion accepted, changed or rejected, and the check that decided it:

## Partitioning

| Read | Plain-table plan | Partitions actually accessed | Same result? |
| --- | --- | --- | --- |
| October count | | | |
| Ticket 100 history | | | |

Would you choose monthly partitions for reporting, ticket support, or monthly retention? Refer to the plans.
