-- Published dispatch plans: one header per plan and one row per route.
-- Row values are copies taken at publication, so later renames or cancellations do not change them.
create table lab07.published_plans (
    id text primary key,
    window_from_utc timestamptz not null,
    window_to_utc timestamptz not null,
    published_at_utc timestamptz not null
);
create table lab07.published_plan_rows (
    plan_id text not null references lab07.published_plans(id),
    route_id text not null,
    route_name text not null,
    departure_count int not null,
    next_departure_utc timestamptz,
    primary key (plan_id, route_id)
);
