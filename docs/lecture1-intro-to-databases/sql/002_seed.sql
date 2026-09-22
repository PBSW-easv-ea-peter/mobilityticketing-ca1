insert into operators (id, name) values
    ('OP-METRO', 'City Metro'),
    ('OP-BUS', 'City Bus')
on conflict do nothing;

insert into routes (id, operator_id, city_id, mode, short_name) values
    ('LINE-M2', 'OP-METRO', 'CPH', 'metro', 'M2'),
    ('LINE-5C', 'OP-BUS', 'CPH', 'bus', '5C')
on conflict do nothing;

insert into stops (id, city_id, name) values
    ('STOP-NORREPORT', 'CPH', 'Nørreport'),
    ('STOP-KONGENS-NYTORV', 'CPH', 'Kongens Nytorv'),
    ('STOP-AIRPORT', 'CPH', 'Copenhagen Airport'),
    ('STOP-CENTRAL', 'CPH', 'Copenhagen Central Station')
on conflict do nothing;


-- Add route_stops rows after deciding the key.
alter table route_stops
add primary key (route_id, stop_sequence);

-- Add at least two trips per route on the same service date.
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

-- seed data in the trips table
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