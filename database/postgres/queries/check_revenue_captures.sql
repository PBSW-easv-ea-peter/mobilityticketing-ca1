
-- Function
select * FROM captured_revenue_for_day('OP-METRO', '2026-04-29');

-- Trigger
SELECT
    captured_amount,
    captured_payments
FROM
    daily_revenue_by_operator
WHERE
    operator_id = 'OP-METRO'
    AND revenue_date = '2026-04-29';

-- View
REFRESH MATERIALIZED VIEW daily_captured_revenue;

SELECT
    captured_amount,
    captured_payments
FROM
    daily_captured_revenue
WHERE
    operator_id = 'OP-METRO'
    AND revenue_date = '2026-04-29';