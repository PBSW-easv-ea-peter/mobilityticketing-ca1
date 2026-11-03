
# Implementation lab: The departure search

The timetable now contains three months of trips. A passenger searches for departures on one route. Customer support also needs to look up a ticket's validation history.

| Read | Required result |
|------|-----------------|
| Departure search |	The first 20 scheduled trips for the selected route and UTC day, ordered by departure time and then ID. |
| Monthly report |	The number of validation events in October. |
| Ticket history |	Every validation for ticket 100, across all months. |

Use the [Lecture 8 starter](https://github.com/patrickstolc/mobilityticketing-lecture-8-starter) and follow its README. It supplies PostgreSQL 17, 120,008 trips, two copies of the validation history and commands for running the comparisons. It runs independently of your earlier labs.

Work in `queries/candidate.sql` and `queries/indexes`.sql. Keep the supplied baseline and fixture unchanged. Use `docs/evidence.md` for your results.

## 1. Find the work behind the search

Run the baseline for R-7 on 23 October 2026. It should return 20 departures. The full day contains 38 scheduled trips. Save baseline plans for both R-7 and the busier R-0 using the README commands. For example:

```bash
docker compose exec -T postgres bash /lab/run.sh measure baseline R-7 > docs/baseline-r7.txt
```

The helper runs a warm-up followed by five measured executions. Record the median and range of Execution Time in your evidence table. Find where the plan reads rows, discards rows or sorts them. Pick one part you want to reduce and predict how your change will help.

## 2. Improve the search

1. Rewrite the date condition in candidate.sql as a UTC range. Include midnight at the start of the chosen day and exclude midnight at the start of the next day. Keep the supplied variables, columns, status filter, ordering and limit.
2. Run `check`. It compares the first 20 rows and the full day for both routes, catching a wrong date boundary even when the first page looks correct.
3. Measure the rewritten query before adding an index. What changed in the plan?
4. Test an index on route_id alone.
5. Replace it with one index of your own choice. Consider the route filter, departure ordering and Scheduled status.

Use indexes.sql for your index statements. Test the two candidates separately and keep the primary-key index. Run check and measure both routes with each candidate.

Use AI for a second opinion: Write your own prompt asking for one candidate index. Give the tool your query, table definition, existing indexes and a baseline plan. Test its suggestion or explain why you chose another. Keep the prompt and your decision with your notes.

Compare the plans as well as the timings. Did filtering or sorting disappear? Did fewer buffers get accessed? Record each index's size and explain what it adds to the work of inserting, rescheduling or cancelling a trip.

## 3. Does partitioning help both reads?

Run the supplied partitions command. It compares an ordinary validation table with the same rows divided into September, October and November.

- Check the results. Both layouts should count 40,889 October events and find 12 events for ticket 100. The ticket-history comparison should report zero differences.
- For the October report, name the partitions that appear in the plan.
- For the ticket lookup, explain why PostgreSQL accesses the partitions it does.

Would you choose monthly partitions for these reads? What if the operator also needs to remove old months? Use the plans to support your answer.

Finish your evidence table with the index you would keep, one alternative you rejected and one cost of your choice. Save the SQL changes and plan files in your own repository.