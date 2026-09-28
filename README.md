# MobilityTicketing — Compulsory Assignment 1

## Uge 1 – Normalisering

### Overvejelser
Vi har valgt at fjerne `ticket_code` fra tabellen `Validations`, da `ticket_code` er en redundant funktionel afhængighed, som kan udledes ud fra `ticket_id`.

Ved at fjerne `ticket_code` undgår vi at gemme den samme information flere steder og reducerer dermed risikoen for inkonsistente data.

### TODO
- [ ] Compose  `./docker-compose.yaml`.
- [ ] Connect til Postgres databasen `mobility` på localhost:5432

`mobility` burde nu være sat op med vores schema og seed-data fra `./databse/postgres/init/`.

---
## Uge 2 – Constraints og dataintegritet

### Overvejelser
Vi har opsat forskellige constraints, som sikrer, at der kun kan indsættes gyldige data i databasen.

Eksempler på validering:

- `capacity > 0`, så et `trip` ikke kan have negativ kapacitet.
- `price > 0`, fordi en `payment` ikke kan have negativ værdi.
- `ticket_status`, skal nu ingå i `[Scheduled, Active, Expired, Validated]`.

Derudover er der oprettet tests, som forsøger at indsætte ugyldige data. Disse tests skal fejle, hvilket demonstrerer, at vores constraints fungerer som forventet.

### TODO
- [ ] Kør `./database/postgres/migrations/001_ticketing_integrity.sql`.
- [ ] Kør `./database/postgres/experiments/01_constraints_should_fail.sql` og verificér, at testene fejler som forventet.

---

## Uge 3 – Programmability

### Overvejelser
Vi har implementeret tre forskellige mekanismer til at beregne payments for en operator:

- **Function:** Viser altid de **aktuelle data**, da beregningen udføres, når funktionen kaldes.
- **Materialized View:** Et materialized view kan indeholde **forældede data**, hvis det ikke bliver refreshed efter ændringer i de underliggende tabeller.
- **Trigger:** Triggeren påvirker kun data **efter det tidspunkt, hvor triggeren er blevet oprettet**. I vores tilfælde medregner det ikke payments fra seed-data.

### TODO
- [ ] Kør `./database/postgres/migrations/002_revenue_functions.sql`.
- [ ] Kør `./database/postgres/queries/base_revenue.sql`, for at tjekker udgangspunktet.
- [ ] Kør `./database/postgres/queries/check_revenue_captures.sql`, for at tjekker udgangspunktet.
- [ ] Kør  `./database/postgres/experiments/02_reporting_cases.sql`, for at indsætte payments.
- [ ] Kør `./database/postgres/queries/base_revenue.sql` for at tjekker det nye udgangspunkt, og `./database/postgres/queries/check_revenue_captures.sql` for at tjekker, hvordan de forskellige mekanismer beregner total payments.

---

## Uge 4 – Migration og ændring af relationer

### Overvejelser

Vi har implementeret en migration, hvor `products` får en ny `id`-kolonne, som fungerer som et unik id for hvert produkt.

Herefter tilføjes `product_id` til `tickets`, som fungerer som en ny FK til `products.id`.

Formålet er at ændre relationen mellem `tickets` og `products`, så tickets fremover kan referere direkte til et produkt via dets unikke `id` i stedet for udelukkende at bruge `product_code`.

Den eksisterende `product_code` bevares i første omgang, så den gamle reader/writer-funktionalitet fortsat fungerer. Dette gør det muligt at migrere systemet gradvist uden at bryde den eksisterende funktionalitet.

- **Old Writer:** Opretter en ticket ved hjælp af `product_code`.
- **Old Reader:** Finder produktet via `tickets.product_code = products.code`.
- **New Writer:** Finder produktet via `products.id` og gemmer dette i `tickets.product_id`.
- **New Reader:** Kan fortsat læse produktinformation, mens migrationen understøtter den nye relation.

Migrationen anvender desuden `NOT VALID` på foreign key-constrainten, så constrainten kan oprettes uden først at validere eksisterende data. Nye data skal dog overholde FK-contrainten.

### TODO

- [ ] Kør `./database/postgres/migrations/003_ticket_migration.sql`.
- [ ] Kør `./database/postgres/experiments/03_check_ticket_table_after_migrations.sql`.
- [ ] Kontrollér, at den gamle Writer/Reader stadig fungerer.

---

Vores arbejde for de enkelte uger kan findes i `docs`, og PROGRESSION.md forklarer udviklingen.
