# Domain and workloads

The system aggregates buses, trams, and trains in a city.

Customers can search for routes, view departures and delays, buy digital tickets, and validate tickets when boarding.

Operators can maintain routes and timetables, update products and prices, and view usage and revenue reports.

The main data concepts are:

- users
- operators
- routes
- stops
- vehicles
- trips
- products
- tickets
- payments
- validations
- real-time availability
- reporting aggregates

The system has six important workloads.

## Journey search

Customers search for journeys between stops and expect results quickly, particularly during rush hour.

Search needs information about routes, departures, arrival times, prices, and current availability.

This is primarily a read-heavy workload. Low latency is important. Slightly stale information may be acceptable if the customer still receives a useful set of journey options.

## Ticket purchase

A customer selects a trip and a product and attempts to purchase a ticket.

The system needs to:

1. determine whether the requested trip can still be sold;
2. determine the correct product and price;
3. process payment;
4. create the ticket;
5. record the payment;
6. update the remaining capacity;
7. make the ticket available to the customer.

This is a correctness-critical workload.

## Ticket validation

A passenger presents a ticket when boarding.

The system needs to find the ticket, determine whether it may be accepted, and record the validation.

Validation is latency-sensitive because it happens while passengers are boarding. Reporting does not need to be updated before validation can complete.

## Timetable maintenance

Operators maintain routes, trips, and timetables.

An operator may replace the timetable for an entire route. Changes should eventually become visible to customer journey searches.

## Real-time availability

Customers need an indication of whether capacity is still available.

Availability changes as tickets are purchased and may be read much more frequently than it changes.

A useful design must distinguish between availability that is good enough for journey search and availability that is trustworthy enough to make a purchase decision.

## Reporting

Operators need usage and revenue reports based on ticket, payment, and validation history.

Reporting can tolerate more latency than purchase and validation. Reports do not need to reflect every operational write immediately.