
-- Old Writer
insert into tickets
  (id, user_id, trip_id, ticket_code, status, product_code,
   valid_from_utc, valid_to_utc, price, currency)
values
  ('TICKET-4', 'USER-1', 'TRIP-M2-20260429-0800', 'CODE-M2-0002', 'Active', 'SINGLE',
   '2026-04-29 07:45:00+00', '2026-04-29 10:00:00+00', 36.00, 'DKK');

-- Old Reader
select t.id, t.product_code, p.name, p.price, p.currency
from tickets t
join products p on p.code = t.product_code
where t.id = 'TICKET-4';

SELECT * FROM products p

-- New Writer
insert into tickets
  (id, user_id, trip_id, ticket_code, status, product_id, product_code,
   valid_from_utc, valid_to_utc, price, currency)
select
  'TICKET-5', 'USER-2', 'TRIP-5C-20260429-0900', 'CODE-5C-0002', 'Active',
  p.id, p.code,
  '2026-04-29 08:45:00+00', '2026-04-29 11:00:00+00', 80.00, 'DKK'
from products p
where p.id = '186ae218-8ebe-4342-a71c-15ac29770dc6';  -- DAY (Should be updated to the new uuid of 'DAY')

select t.id, t.product_code, p.name, p.price, p.currency
from tickets t
join products p on p.code = t.product_code
where t.id = 'TICKET-5';
