# 36 – Database (integritetskonstraints)

Kør

```powershell
Get-Content database/postgres/migrations/011_ticketing_integrity.sql | docker compose exec -T postgres psql -U mobility -d mobility
```

i terminalen for at indlæse den nye `011_ticketing_integrity.sql` fil.

## Kort version til dine noter

| Kategori | Hvad betyder det? | Eksempel |
|---|---|---|
| **Direct constraint** | Kan håndhæves direkte af DBMS på data | `Age >= 18` |
| **Unique / exclusion rule** | Forhindrer duplikater eller konflikter mellem rækker | Email skal være unik |
| **Cross-row / external workflow rule** | Kræver flere rækker/tabeller eller applikationsworkflow | Klasse ≤ 30 studerende |
| **Unresolved domain decision** | Domænereglen er endnu ikke afklaret | Må en kunde have flere aktive abonnementer? |

**Huskeregel:**
- Kan SQL-constrainten selv håndhæve det? → Direct/Unique.
- Skal jeg kigge på flere rækker eller en proces? → Cross-row/workflow.
- Ved vi ikke engang, hvad reglen skal være? → Unresolved domain decision.

## capacity cannot be negative;

På trip-tabellen er der en attribut der hedder capacity. Dette er en integer.

Den beskriver køretøjets/transportmidlets kapacitet på denne gennemkørsel af en rute.

En kapacitet er naturligvis et positivt tal, da man ikke kan have -3 sæder i en bus.

Det er en **direct constraint**, da det kan løses ved en simpel constraint på trip-tabellen:

```sql
capacity INTEGER CHECK (capacity >= 0)
```

Så i `011_ticketing_integrity.sql` har vi allerede:

```sql
alter table trips
    alter column capacity set not null,
    alter column reserved_seats set not null,
    add constraint trips_capacity_non_negative
        check (capacity >= 0),
    add constraint trips_reserved_seats_valid
        check (reserved_seats between 0 and capacity);
```

Og når vi får styr på at få det over i docker får vi:

```sql
update trips
set capacity = -1
where id = 'TRIP-M2-20260429-0800';
```

```
ERROR:  new row for relation "trips" violates check constraint "trips_capacity_non_negative"
Failing row contains (TRIP-M2-20260429-0800, LINE-M2, 2026-04-29, 2026-04-29 08:00:00+00, Scheduled, -1, 2).

SQL state: 23514
Detail: Failing row contains (TRIP-M2-20260429-0800, LINE-M2, 2026-04-29, 2026-04-29 08:00:00+00, Scheduled, -1, 2).
```

Og success:

```sql
update trips
set capacity = 20
where id = 'TRIP-M2-20260429-0800';
```

```
UPDATE 1

Query returned successfully in 110 msec.
```

## Reserved seats cannot be negative or greater than capacity.

På trip-tabellen må reserved_seats ikke være negativ eller højere end capacity.

Det giver god mening: der kan ikke være reserveret 50 pladser i en bus med plads til 20. Man kan heller ikke have -3 reservationer.

Denne regel er "to i én":

At reserved_seats ikke må være negativ, kan løses med en **direct constraint**:

```sql
reserved_seats INTEGER CHECK (reserved_seats >= 0)
```

Tjekket på om der er høj nok kapacitet er et **cross-column**-tjek, da den tilladte range for reserved_seats bestemmes af capacity:

```sql
reserved_seats INTEGER CHECK (reserved_seats <= capacity)
```

De to delconstraints kan slås sammen til:

```sql
check (reserved_seats between 0 and capacity)
```

```sql
alter table trips
    alter column capacity set not null,
    alter column reserved_seats set not null,
    add constraint trips_capacity_non_negative
        check (capacity >= 0),
    add constraint trips_reserved_seats_valid
        check (reserved_seats between 0 and capacity);
```

```sql
update trips
set reserved_seats = capacity + 1
where id = 'TRIP-M2-20260429-0800';
```

```
ERROR:  new row for relation "trips" violates check constraint "trips_reserved_seats_valid"
Failing row contains (TRIP-M2-20260429-0800, LINE-M2, 2026-04-29, 2026-04-29 08:00:00+00, Scheduled, 20, 21).

SQL state: 23514
Detail: Failing row contains (TRIP-M2-20260429-0800, LINE-M2, 2026-04-29, 2026-04-29 08:00:00+00, Scheduled, 20, 21).
```

Og før satte vi jo kapaciteten til 20 så 15 burde være okay:

```sql
update trips
set reserved_seats = 15
where id = 'TRIP-M2-20260429-0800';
```

```
UPDATE 1

Query returned successfully in 98 msec.
```

## Ticket price and payment amount cannot be negative.

Priser og betalingsbeløb kan ikke være negative.

Det giver fin mening — vi har ikke et refunderingsbegreb, men det ville selvfølgelig være et positivt tal bare den anden vej.

Dette minder meget om capacity og er en direct constraint, med den undtagelse at datatypen er numeric i stedet for integer, da priser og beløb typisk skal kunne have decimaler (fx øre/cents).

Den påvirker attributter på tværs af flere tabeller: price på product og ticket, samt amount på payment:

```sql
price NUMERIC CHECK (price >= 0) -- product, ticket
amount NUMERIC CHECK (amount >= 0) -- payment
```

```sql
alter table payments
    add constraint payments_amount_non_negative
        check (amount >= 0);

alter table products
    add constraint products_price_non_negative
        check (price >= 0);

alter table tickets
    add constraint tickets_price_non_negative
        check (price >= 0);
```

Så genindlæs `011_ticketing_integrity.sql` med:

```powershell
Get-Content database/postgres/migrations/011_ticketing_integrity.sql | docker compose exec -T postgres psql -U mobility -d mobility
```

### Test af payment amount

```sql
insert into payments (
    id, user_id, ticket_id, external_payment_reference,
    amount, currency, status
) values (
    'PAYMENT-NEGATIVE-AMOUNT', 'USER-1', 'TICKET-1',
    'gateway-capture-negative', -36, 'DKK', 'Captured'
);
```

```
ERROR:  new row for relation "payments" violates check constraint "payments_amount_non_negative"
Failing row contains (PAYMENT-NEGATIVE-AMOUNT, USER-1, TICKET-1, gateway-capture-negative, -36, DKK, Captured, 2026-09-03 16:20:26.908518+00).

SQL state: 23514
Detail: Failing row contains (PAYMENT-NEGATIVE-AMOUNT, USER-1, TICKET-1, gateway-capture-negative, -36, DKK, Captured, 2026-09-03 16:20:26.908518+00).
```

```sql
insert into payments (
    id, user_id, ticket_id, external_payment_reference,
    amount, currency, status
) values (
    'PAYMENT-NEGATIVE-AMOUNT', 'USER-1', 'TICKET-1',
    'gateway-capture-negative', 57, 'DKK', 'Captured'
);
```

```
INSERT 0 1

Query returned successfully in 99 msec.
```

### Test af product

```sql
insert into products (
    code, name, price, currency
) values (
    'INVALID-PRODUCT', 'Invalid Product', -1, 'DKK'
);
```

```
ERROR:  new row for relation "products" violates check constraint "products_price_non_negative"
Failing row contains (INVALID-PRODUCT, Invalid Product, -1, DKK).

SQL state: 23514
Detail: Failing row contains (INVALID-PRODUCT, Invalid Product, -1, DKK).
```

```sql
insert into products (
    code, name, price, currency
) values (
    'VALID-PRODUCT', 'valid Product', 100, 'DKK'
);
```

```
INSERT 0 1

Query returned successfully in 91 msec.
```

### Test af ticket

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'TICKET-NEGATIVE-PRICE', 'USER-1', 'TRIP-M2-20260429-0800',
    'CODE-NEGATIVE-PRICE', 'Active', 'INVALID-PRODUCT',
    '2026-04-29 08:00:00+00', '2026-04-29 09:00:00+00', -36, 'DKK'
);
```

```
ERROR:  new row for relation "tickets" violates check constraint "tickets_price_non_negative"
Failing row contains (TICKET-NEGATIVE-PRICE, USER-1, TRIP-M2-20260429-0800, CODE-NEGATIVE-PRICE, Active, INVALID-PRODUCT, 2026-04-29 08:00:00+00, 2026-04-29 09:00:00+00, -36, DKK).

SQL state: 23514
Detail: Failing row contains (TICKET-NEGATIVE-PRICE, USER-1, TRIP-M2-20260429-0800, CODE-NEGATIVE-PRICE, Active, INVALID-PRODUCT, 2026-04-29 08:00:00+00, 2026-04-29 09:00:00+00, -36, DKK).
```

succes:

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'TICKET-POSITIVE-PRICE', 'USER-1', 'TRIP-M2-20260429-0800',
    'CODE-POSITIVE-PRICE', 'Active', 'VALID-PRODUCT',
    '2026-04-29 08:00:00+00', '2026-04-29 09:00:00+00', 36, 'DKK'
);
```

```
INSERT 0 1

Query returned successfully in 105 msec.
```

## tickets must reference existing users, trips, and products;

Billetter skal tilhøre eksisterende brugere og være tilknyttet en eksisterende gennemkørsel og et produkt.

Vi må antage, at man skal have oprettet en profil for at kunne købe en billet — altså ingen gæsteprofiler eller mulighed for at købe billetter via andre kanaler. I gamle dage kunne man jo bare betale chaufføren, når man steg på bussen, men den mulighed findes ikke i denne model.

Det er en **cross-table constraint** (referential integrity), i modsætning til de foregående punkter — værdien i tickets skal matche en eksisterende række i en *anden* tabel, fremfor at tjekke mod sig selv.

user_id- og trip_id-referencerne lå allerede i skabelonen fra start.

*(evt en kort sludder om delvis kørsel)*

```sql
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
```

Test hvor trip ikke findes:

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'T-INVALID-TRIP', 'USER-1', 'TRIP-DOES-NOT-EXIST',
    'CODE-INVALID-TRIP', 'Active', 'SINGLE',
    '2026-04-29 08:00:00+00', '2026-04-29 09:00:00+00', 36, 'DKK'
);
```

```
ERROR:  insert or update on table "tickets" violates foreign key constraint "tickets_trip_fk"
Key (trip_id)=(TRIP-DOES-NOT-EXIST) is not present in table "trips".

SQL state: 23503
Detail: Key (trip_id)=(TRIP-DOES-NOT-EXIST) is not present in table "trips".
```

Test hvor product ikke findes:

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'T-INVALID-PRODUCT', 'USER-1', 'TRIP-M2-20260429-0800',
    'CODE-INVALID-PRODUCT', 'Active', 'PRODUCT-DOES-NOT-EXIST',
    '2026-04-29 08:00:00+00', '2026-04-29 09:00:00+00', 36, 'DKK'
);
```

```
ERROR:  insert or update on table "tickets" violates foreign key constraint "tickets_product_fk"
Key (product_code)=(PRODUCT-DOES-NOT-EXIST) is not present in table "products".

SQL state: 23503
Detail: Key (product_code)=(PRODUCT-DOES-NOT-EXIST) is not present in table "products".
```

Test hvor user ikke findes:

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'T-INVALID-USER', 'USER-DOES-NOT-EXIST', 'TRIP-M2-20260429-0800',
    'CODE-INVALID-USER', 'Active', 'SINGLE',
    '2026-04-29 08:00:00+00', '2026-04-29 09:00:00+00', 36, 'DKK'
);
```

```
ERROR:  insert or update on table "tickets" violates foreign key constraint "tickets_user_fk"
Key (user_id)=(USER-DOES-NOT-EXIST) is not present in table "users".

SQL state: 23503
Detail: Key (user_id)=(USER-DOES-NOT-EXIST) is not present in table "users".
```

Succes:

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'T-VALID-1', 'USER-1', 'TRIP-M2-20260429-0800',
    'CODE-VALID-1', 'Active', 'SINGLE',
    '2026-04-29 08:00:00+00', '2026-04-29 09:00:00+00', 36, 'DKK'
);
```

```
INSERT 0 1

Query returned successfully in 104 msec.
```

## payments must reference existing tickets

En betaling skal være tilknyttet en eksisterende billet — man kan ikke betale for en billet, der ikke findes i systemet.

Det er en cross-table constraint (referential integrity), samme type som tickets_trip_fk og tickets_product_fk — værdien i payments.ticket_id skal matche en eksisterende række i tickets.

```sql
alter table payments
    add constraint payments_ticket_fk
        foreign key (ticket_id) references tickets(id);
```

```sql
insert into payments (
    id, user_id, ticket_id, external_payment_reference,
    amount, currency, status
) values (
    'PAYMENT-UNKNOWN-TICKET', 'USER-1', 'NO-SUCH-TICKET',
    'gateway-capture-invalid', 36, 'DKK', 'Captured'
);
```

```
ERROR:  insert or update on table "payments" violates foreign key constraint "payments_ticket_fk"
Key (ticket_id)=(NO-SUCH-TICKET) is not present in table "tickets".

SQL state: 23503
Detail: Key (ticket_id)=(NO-SUCH-TICKET) is not present in table "tickets".
```

Succes:

```sql
insert into payments (
    id, user_id, ticket_id, external_payment_reference,
    amount, currency, status
) values (
    'PAYMENT-VALID-1', 'USER-1', 'T-VALID-1',
    'gateway-capture-valid', 36, 'DKK', 'Captured'
);
```

```
INSERT 0 1

Query returned successfully in 105 msec.
```

## 1. validations must reference existing tickets

En validering skal være tilknyttet en eksisterende billet.

Her er en krølle: validations gemmer både ticket_id og ticket_code som separate kolonner. En almindelig FK på ticket_id alene ville ikke sikre, at den medfølgende ticket_code rent faktisk tilhører samme billet — begge værdier kunne hver for sig være gyldige, men tilhøre to forskellige billetter.

Løsningen er en **sammensat cross-table constraint**: (ticket_id, ticket_code) skal matche samme række i tickets, understøttet af en unik nøgle på (id, ticket_code):

```sql
alter table tickets
    add constraint tickets_id_ticket_code_unique
        unique (id, ticket_code);

alter table validations
    add constraint validations_ticket_fk
        foreign key (ticket_id, ticket_code) references tickets(id, ticket_code);
```

## 2. ticket codes must identify tickets unambiguously

Betyder, at man ud fra en ticket code skal kunne udpege en specifik ticket.

Det må betyde, at en ticket code skal være unik — for hvis der er to tickets med samme ticket code, ved man ikke hvem af dem det er.

Det er en unique constraint ("Unique / exclusion rule"), ikke en direct constraint — reglen handler ikke om værdien i sig selv, men om forholdet mellem rækker: værdien i én række må ikke være den samme som i en anden.

```sql
alter table tickets
    add constraint tickets_ticket_code_unique
        unique (ticket_code);
```

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
)
select
    'T-DUPLICATE-CODE', user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
from tickets
where id = 'TICKET-1';
```

```
ERROR:  duplicate key value violates unique constraint "tickets_ticket_code_unique"
Key (ticket_code)=(CODE-M2-0001) already exists.

SQL state: 23505
Detail: Key (ticket_code)=(CODE-M2-0001) already exists.
```

## 3. ticket validity cannot end before it begins

Betyder, at valid_to_utc skal ligge efter valid_from_utc — en billet kan ikke ophøre med at være gyldig, før dens gyldighed overhovedet er startet.

Det giver god mening: en billet der er gyldig fra kl. 09:00 til kl. 08:00 samme dag, er en modsigelse — gyldighedsvinduet findes reelt ikke.

Det er en cross-column constraint, samme type som reserved_seats <= capacity — begge kolonner ligger på samme række, og den ene sætter grænsen for den anden.

```sql
alter table tickets
    add constraint tickets_validity_window_valid
        check (valid_to_utc > valid_from_utc);
```

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'T-REVERSED', 'USER-1', 'TRIP-M2-20260429-0800',
    'CODE-REVERSED', 'Active', 'SINGLE',
    '2026-04-29 09:00:00+00', '2026-04-29 08:00:00+00', 36, 'DKK'
);
```

```
ERROR:  new row for relation "tickets" violates check constraint "tickets_validity_window_valid"
Failing row contains (T-REVERSED, USER-1, TRIP-M2-20260429-0800, CODE-REVERSED, Active, SINGLE, 2026-04-29 09:00:00+00, 2026-04-29 08:00:00+00, 36, DKK).

SQL state: 23514
Detail: Failing row contains (T-REVERSED, USER-1, TRIP-M2-20260429-0800, CODE-REVERSED, Active, SINGLE, 2026-04-29 09:00:00+00, 2026-04-29 08:00:00+00, 36, DKK).
```

En almindelig, fremadrettet gyldighedsperiode lykkes:

```sql
insert into tickets (
    id, user_id, trip_id, ticket_code, status,
    product_code, valid_from_utc, valid_to_utc, price, currency
) values (
    'T-REVERSED', 'USER-1', 'TRIP-M2-20260429-0800',
    'CODE-REVERSED', 'Active', 'SINGLE',
    '2026-04-29 08:00:00+00', '2026-04-29 09:00:00+00', 36, 'DKK'
);
```

```
INSERT 0 1

Query returned successfully in 95 msec.
```

## 4. status values must come from an accepted set

status-kolonner (fx på trips, payments) må kun indeholde værdier fra en aftalt liste — ikke en vilkårlig tekststreng.

Vi har ikke modtaget en officiel liste fra domænet, så dette er en **unresolved domain decision**, ikke noget SQL kan løse endnu.

Foreløbigt bud:

- **Trip status**: planlagt, igang, aflyst, afbrudt, gennemført
- **Payment status**: oprettet, igang, betalt, mislykket, refunderet

aflyst og afbrudt er holdt adskilt, da en trip kan aflyses før afgang (fx pga. sne) uden passagerer om bord, mens en afbrudt trip stopper undervejs. mislykket er tilføjet på betalingssiden, så en afvist/fejlet betaling har en status at lande i.

Hvis listen lå fast, ville den blive en **direct constraint** (den tjekker kun værdien i den enkelte kolonne, ligesom capacity >= 0):

```sql
status TEXT CHECK (status IN ('planlagt', 'igang', 'aflyst', 'afbrudt', 'gennemført'))
```

Listen er ikke endelig og skal valideres mod domænet, før den låses fast som ovenstående eller en separat statustabel.

## 5. external payment references must not accidentally represent the same payment more than once.

Betyder, at én betaling hos Stripe (external_payment_reference) ikke ved en fejl skal kunne blive registreret som to forskellige rækker i payments.

Det giver god mening: hvis den samme Stripe-reference (fx en PaymentIntent-id som pi_3Mabc123XYZ) dukker op to gange — fx pga. en fejl i integrationen eller et dobbelt-forsøgt API-kald — risikerer man at registrere samme betaling to gange, hvilket kan give forkerte regnskabstal eller dobbelt kreditering.

Det er en unique constraint ("Unique / exclusion rule"), samme kategori som invariant 7 (ticket_code).

```sql
alter table payments
    add constraint payments_external_reference_unique
        unique (external_payment_reference);
```

```sql
insert into payments (
    id, user_id, ticket_id, external_payment_reference,
    amount, currency, status
) values (
    'PAYMENT-DUPLICATE-REFERENCE', 'USER-1', 'TICKET-1',
    'gateway-capture-0001', 36, 'DKK', 'Captured'
);
```

```
ERROR:  duplicate key value violates unique constraint "payments_external_reference_unique"
Key (external_payment_reference)=(gateway-capture-0001) already exists.

SQL state: 23505
Detail: Key (external_payment_reference)=(gateway-capture-0001) already exists.
```

## Currency must be present and consistently represented

currency skal være udfyldt på products, tickets og payments, og skal være skrevet på samme måde hver gang — ikke DKK ét sted og dkk, Danske kroner eller ",-" et andet.

"Present" og "consistently represented" hænger reelt sammen: hvis der findes en fast liste af gyldige valutaer, fanger en check (currency in (...)) automatisk både manglende værdi og forkert stavet værdi i én omgang — null og fritekst som "Danske kroner" matcher aldrig listen. Det egentlige åbne spørgsmål er derfor ikke kun om feltet skal være udfyldt, men om systemet skal understøtte flere valutaer (med behov for vekselkursstyring), eller kun DKK i denne fase — det er en unresolved domain decision.

Hvis systemet kun kører i DKK lige nu, bliver det en direct constraint:

```sql
currency TEXT NOT NULL CHECK (currency = 'DKK')
```

Hvis flere valutaer skal understøttes fra start, bliver det en fast liste over ISO 4217-koder, samme type som forslaget til invariant 9:

```sql
currency TEXT NOT NULL CHECK (currency IN ('DKK', 'EUR', 'SEK'))
```

Listen (eller beslutningen om kun DKK) skal fastlægges, før constraint'en kan skrives endeligt.
