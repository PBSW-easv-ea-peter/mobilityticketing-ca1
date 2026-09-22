# Mobility Ticketing – Lecture 1 Implementation Lab
## Solutions and Justifications

### Introduction
The platform brings together bus, tram, and train services within a single city. Customers search for journeys between two stops, see departure times and any delays, buy a digital ticket for a specific departure, and have the ticket validated when they board. Operators maintain routes and timetables, update which products and prices are offered, and track usage and revenue per route.
An operator is the company that runs one or more routes — for example a metro company or a bus company. A route is a fixed, named connection that visits a particular sequence of stops; each individual departure on a given day is a separate run with its own scheduled time and its own status. The city transport context therefore revolves around two things: the customer's question ("when does something run, and where does it stop?") and the operator's ("what is the current plan, and how do I change it?").

### Task 1
Decide the primary key of the route-stop relation and explain the decision.

#### Argumentation
With the supplied stops from the original dataset, I saw a greater chance for a bus-route starting and stopping in CPH-Airport, and taking the route:

STOP-AIRPORT -> STOP-KONGENS-NYTORV -> STOP-NØRREPORT -> STOP-CENTRAL -> STOP-AIRPORT

Therefore I choose to define a combined PK with (route_id, stop_sequence). But with a larger dataset, and the client having a one-stop policy for the routes, then a composite PK of (route_id, stop_id) would make more sense.

| Candidate key | Unambiguous? | Key type |
|---|---|---|
| `route_id` alone | no | — |
| `stop_id` alone | no | — |
| `route_id`, `stop_sequence` | yes | Candidate key |
| `route_id`, `stop_id` | yes — if stops do not recur on the same route | Conditional candidate key |
| `route_id`, `stop_id`, `stop_sequence` | yes | Superkey, but not a candidate key |

#### SQL Solution
```sql
create table route_stops (
    route_id text not null references routes(id),
    stop_id text not null references stops(id),
    stop_sequence integer not null,
    -- TODO: choose and add the primary key.
    -- Explain whether a stop may occur more than once on the same route.
    primary key (route_id, stop_sequence),
    constraint route_stops_sequence_positive check (stop_sequence > 0)
);
```

### Task 2
Insert the supplied seed data.
### Argumentation
To be able to seed the data, i had to create queries to seed data in the tables route_stops for both lines, with at least 2 stops per line, and data in the trips table.

Furthermore i've added the final command in each query 
```sql
on conflict do nothing;
```
to prevent the whole SQL action to fail, and risk incomplete data in the database, or seed doublettes by accident.
### SQL Solution
#### Seed queries for route_stop table
```sql
insert into route_stops (route_id, stop_id, stop_sequence) values
    ('LINE-5C', 'STOP-AIRPORT',          1),
    ('LINE-5C', 'STOP-KONGENS-NYTORV',   2),
    ('LINE-5C', 'STOP-CENTRAL',           3),
    ('LINE-5C', 'STOP-NORREPORT',         4),
    ('LINE-5C', 'STOP-AIRPORT',           5)
on conflict do nothing;

insert into route_stops (route_id, stop_id, stop_sequence) values
    ('LINE-M2', 'STOP-NORREPORT',        1),
    ('LINE-M2', 'STOP-KONGENS-NYTORV',   2),
    ('LINE-M2', 'STOP-CENTRAL',           3)
on conflict do nothing;
```

#### Seed query for trips table
```sql
insert into trips (
    id,
    route_id,
    service_date,
    scheduled_departure_utc,
    status
) values
    ('TRIP-M2-1', 'LINE-M2', '2026-08-28', '2026-08-28 08:00:00+00', 'scheduled'),
    ('TRIP-M2-2', 'LINE-M2', '2026-08-28', '2026-08-28 08:30:00+00', 'scheduled'),
    ('TRIP-5C-1', 'LINE-5C', '2026-08-28', '2026-08-28 09:00:00+00', 'scheduled'),
    ('TRIP-5C-2', 'LINE-5C', '2026-08-28', '2026-08-28 09:30:00+00', 'scheduled')
on conflict do nothing;
```

### Task 3
Write the three workload queries, and the representative results. 
The queries cover upcoming trips, the ordered stops of a route, and the number of trips associated with each route.
### Argumentation
The first workload query was already provided as part of the assignment. It is used to retrieve upcoming trips for a specific route after a given point in time.

The second query retrieves all stops belonging to a specific route and presents them in the correct order according to their stop_sequence.

The third query counts the number of trips for each route on a given service date. A LEFT JOIN is used instead of an INNER JOIN to ensure that routes with no trips on the specified service date are also included in the result. In these cases, COUNT(t.id) returns 0.
### SQL Solution
#### -- Query 1: upcoming trips
```sql
select
    t.id,
    t.scheduled_departure_utc,
    t.status
from trips t
where t.route_id = :route_id
  and t.scheduled_departure_utc >= :after_utc
order by t.scheduled_departure_utc
limit 20;
```
#### Query 2: ordered route stops
```sql
select
    rs.route_id,
    rs.stop_sequence,
    s.id,
    s.name
from route_stops rs
join stops s
    on rs.stop_id = s.id
where rs.route_id = :route_id
order by rs.stop_sequence
```

#### Query 3: routes and trip count, including routes with zero trips
```sql
select
    r.id,
    r.short_name,
    count(t.id) as trip_count
from routes r
left join trips t
    on r.id = t.route_id
   and t.service_date = :service_date
group by
    r.id,
    r.short_name
order by r.id;
```


### Task 4
Compare the implemented schema with your ER diagram and record any difference.

#### Argumentation

Overall, the implemented schema and our ER diagram describe the same core domain, but a few deliberate differences were introduced during implementation. The table below summarises the differences and the reasoning behind each.

| # | ER diagram assumption | Implemented schema | Reason |
|---|---|---|---|
| 1 | `trips` has `departure_time` and `arrival_time` | `trips` has `scheduled_departure_utc` and `status` only | The lab requires trip status ("scheduled" / "cancelled" …) for the upcoming-trip query. We kept arrival out because no workload query uses it yet. |
| 2 | `trips` is linked to a `vehicle` (FK `vehicle_id`) | No `vehicle` table in the implementation | A separate `vehicle` table was modelled in the ER diagram to support the "Find vehicle assigned to a trip" access pattern, but the lab scope explicitly excludes capacity/availability tracking, so it was omitted to keep the model minimal. |
| 3 | `routes` carries `name` and `transport_type` | `routes` carries `city_id` and `short_name` | The lab uses `short_name` (e.g. "5C", "M2") for display in query results, and `city_id` to scope the platform to one city. Our ER diagram modelled `transport_type` and a `name` instead. We adopted the lab's columns because they are what the seed data and the three workload queries depend on. |
| 4 | `stops` has `city`, `name`, and `address` | `stops` has `city_id` and `name` | Our ER diagram modelled a free-text `city` on each stop and an `address`. The lab replaces these with a single `city_id` referencing an (unimplemented) city table. We kept `city_id` to match the lab schema; modelling the address or a zone-based fare structure is explicitly deferred. |
| 5 | `zone` attribute considered on stops/routes for fare calculation | Not implemented | A zone-based fare model is a plausible future requirement, but pricing, products, and payments are explicitly out of scope for this lab, so no zone column was added. |

The remaining structure is consistent: the five implemented tables (`operators`, `routes`, `stops`, `route_stops`, `trips`) map one-to-one to the corresponding entities and the `route_stops` associative relation in the ER diagram, and the primary-key choice for `route_stops` is identical (composite `(route_id, stop_sequence)`).

#### What the implementation proves, and what remains unknown

**Proves**
- The relational baseline supports the three required workload queries.
- Seed data is idempotent (`on conflict do nothing`) and recreatable from an empty database.
- The `route_stops` PK choice handles a route that visits the same stop more than once.
- Normalization to 3NF is preserved — no transitive dependencies.

**Remains unknown**
- Whether the model scales under the read-heavy journey-search access pattern (no performance indexes yet).
- Whether `city_id` / a free-text city / a zone column is the right modelling choice once pricing is introduced.
- How ticket purchase, validation, and real-time availability will be supported — the Access Pattern Map shows these are not yet covered by any table.

#### Access Pattern coverage (from the Access Pattern Map)

| Access pattern | Supported by the 5 implemented tables? |
|---|---|
| Upcoming trips for a route | Query 1 |
| Ordered stops on a route | Query 1 and Query 2 |
| Trips per route on a service date | Query 3 |
| Find vehicle assigned to a trip | No `vehicle` table |
| Journey search (between two stops) | Partially — Query 1 + Query 2, but no two-stop search yet |
| Ticket purchase | No `products`, `tickets`, `payments` tables |
| Ticket validation | No `validations` table |
| Timetable updates | `routes`, `stops`, `route_stops`, `trips` can be created/updated |
| Real-time availability | No capacity tracking on trips |
| Reporting | No aggregate/reporting tables |


