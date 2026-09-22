begin;

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
    if not exists (select 1 from pg_constraint where conname = 'payments_user_fk') then
        alter table payments
            add constraint payments_user_fk
                foreign key (user_id) references users(id);
    end if;

    if not exists (select 1 from pg_constraint where conname = 'payments_ticket_fk') then
        alter table payments
            add constraint payments_ticket_fk
                foreign key (ticket_id) references tickets(id);
    end if;

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
    if not exists (select 1 from pg_constraint where conname = 'tickets_ticket_code_unique') then
        alter table tickets
            alter column ticket_code set not null,
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
    -- Composite candidate key backing validations_ticket_fk below: ensures (id, ticket_code)
    -- is a valid FK target, so a validation cannot combine one ticket's id with another
    -- ticket's code.
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

-- TODO: status (accepted set of values) and currency (present + consistently represented).
-- Both are unresolved domain decisions per the integrity map -- not yet agreed with the domain.
-- Name every constraint so tests and later migrations can identify it.

commit;
