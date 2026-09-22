Operator (PK operatorId text, name text)
Route (PK route_id text, operator_id FK, name text, transport_type text)
Stop (PK stop_id text, city text, name text, adress text) city/zone - hvad er bedst?
Route_Stops (route_id text, stop_id text, stop_sequence integer)
Trip (PK trip_id text, route_id FK, FK vehicle_id, departure_time timestamptz, arrival_time timestamptz)
Vehicle (PK vehicle_id text, vehicle_type text, capacity integer)