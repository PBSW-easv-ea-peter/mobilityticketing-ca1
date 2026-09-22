-- Query 1: upcoming trips
select
    t.id,
    t.scheduled_departure_utc,
    t.status
from trips t
where t.route_id = 'LINE-5C'
  and t.scheduled_departure_utc >= '2026-04-29 10:00:00+02'
order by t.scheduled_departure_utc
limit 20;

-- Query 2: ordered route stops
-- TODO: join route_stops to stops and order by stop_sequence.
select
    rs.route_id,
    rs.stop_sequence,
    s.id,
    s.name
from route_stops rs
join stops s
    on rs.stop_id = s.id
where rs.route_id = 'LINE-5C'
order by rs.stop_sequence;

-- Query 3: routes and trip count, including routes with zero trips
-- TODO: preserve routes with no matching trips for the supplied service date.
select
    r.id,
    r.short_name,
    count(t.id) as trip_count
from routes r
left join trips t
    on r.id = t.route_id
   and t.service_date = '2026-04-29'
group by
    r.id,
    r.short_name
order by r.id;
