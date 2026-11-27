# MobilityTicketing — Databases for Developers

Repoet samler vores arbejde fra ugelabsene. Compulsory Assignment 1 dækker uge 1–4. **Compulsory Assignment 2** dækker NoSQL (L5), Redis (L6), route overview (L7), query optimization (L8) og transactions (L9).

**Til review af CA2: start i [`docs/ca2-review-guide.md`](docs/ca2-review-guide.md).** Den har setup og reset for hver lab, links til kode og evidens, to beslutninger og en begrænsning.

Krav: Docker Desktop (Compose v2). L6 kræver også .NET 10 SDK. Alt nulstilles med `docker compose down -v`.

---

## Uge 1 – Normalisering

### Overvejelser
Vi har valgt at fjerne `ticket_code` fra tabellen `Validations`, da `ticket_code` er en redundant funktionel afhængighed, som kan udledes ud fra `ticket_id`.

Ved at fjerne `ticket_code` undgår vi at gemme den samme information flere steder og reducerer dermed risikoen for inkonsistente data.

### Sådan kører du det
- Compose  `./docker-compose.yaml`.
- Connect til Postgres databasen `mobility` på localhost:5432

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

### Sådan kører du det
- Kør `./database/postgres/migrations/001_ticketing_integrity.sql`.
- Kør `./database/postgres/experiments/01_constraints_should_fail.sql` og verificér, at testene fejler som forventet.

---

## Uge 3 – Programmability

### Overvejelser
Vi har implementeret tre forskellige mekanismer til at beregne payments for en operator:

- **Function:** Viser altid de **aktuelle data**, da beregningen udføres, når funktionen kaldes.
- **Materialized View:** Et materialized view kan indeholde **forældede data**, hvis det ikke bliver refreshed efter ændringer i de underliggende tabeller.
- **Trigger:** Triggeren påvirker kun data **efter det tidspunkt, hvor triggeren er blevet oprettet**. I vores tilfælde medregner det ikke payments fra seed-data.

### Sådan kører du det
- Kør `./database/postgres/migrations/002_revenue_functions.sql`.
- Kør `./database/postgres/queries/base_revenue.sql`, for at tjekker udgangspunktet.
- Kør `./database/postgres/queries/check_revenue_captures.sql`, for at tjekker udgangspunktet.
- Kør  `./database/postgres/experiments/02_reporting_cases.sql`, for at indsætte payments.
- Kør `./database/postgres/queries/base_revenue.sql` for at tjekker det nye udgangspunkt, og `./database/postgres/queries/check_revenue_captures.sql` for at tjekker, hvordan de forskellige mekanismer beregner total payments.

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

### Sådan kører du det

- Kør `./database/postgres/migrations/003_ticket_migration.sql`.
- Kør `./database/postgres/experiments/03_check_ticket_table_after_migrations.sql`.
- Kontrollér, at den gamle Writer/Reader stadig fungerer.

---

## Uge 7 – Route overview (EF Core)

Opgaven: [`docs/lecture7-route-overview/lab.md`](docs/lecture7-route-overview/lab.md). Krav: [`contract.md`](docs/lecture7-route-overview/contract.md). Designnote: [`design.md`](docs/lecture7-route-overview/design.md). Målinger: [`docs/evidence/lecture07/README.md`](docs/evidence/lecture07/README.md).

Lecture 7 har sin egen database (`postgres-l07`, database `mobility_l07`, schema `lab07`) og kører via Docker (.NET 8 SDK i imaget).

### TODO
- [ ] Start databasen: `docker compose up -d --wait postgres-l07`.
- [ ] Byg app og tests: `docker compose build app tests` (også efter ændringer i kode eller SQL).
- [ ] Nulstil fixturen: `docker compose run --rm app reset`.
- [ ] Kør lazy-baseline: `docker compose run --rm app baseline` (SQL og resultat gemmes i `docs/evidence/lecture07/logs/`).
- [ ] Kør tests: `docker compose run --rm tests --filter Category=Starter` (også `Acceptance` og `Student`).
- [ ] Mere data: `docker compose run --rm app scale` (200 ruter / 100.000 trips).

---

## Uge 8 – Query optimization

Opgaven: [`docs/lecture8-query-optimization/lab.md`](docs/lecture8-query-optimization/lab.md). Starterens README: [`starter-README.md`](docs/lecture8-query-optimization/starter-README.md). Evidence: [`docs/evidence/lecture08/evidence.md`](docs/evidence/lecture08/evidence.md).

Lecture 8 har sin egen database (`postgres-l08`, database `mobility_l08`, schema `lab08`, ingen host-port). Vi arbejder i `database/postgres/lecture08/queries/candidate.sql` og `indexes.sql`; `run.sh`, `setup.sql` og `baseline.sql` holdes uændrede.

### TODO
- [ ] Start databasen: `docker compose up -d --wait postgres-l08`.
- [ ] Nulstil fixturen: `docker compose exec -T postgres-l08 bash /lab/run.sh reset`.
- [ ] Kør og tjek: `docker compose exec -T postgres-l08 bash /lab/run.sh run baseline R-7` og `... run.sh check`.
- [ ] Mål: `docker compose exec -T postgres-l08 bash /lab/run.sh measure baseline R-7 > docs/evidence/lecture08/baseline-r7.txt` (også `R-0` og `candidate`).
- [ ] Indexes: `docker compose exec -T postgres-l08 bash /lab/run.sh indexes`.
- [ ] Partitioning: `docker compose exec -T postgres-l08 bash /lab/run.sh partitions > docs/evidence/lecture08/partition-plans.txt`.

---

## Uge 11 – Data storage

Opgaven: [`docs/lecture11-data-storage/lab.md`](docs/lecture11-data-storage/lab.md). Starterens README: [`starter-README.md`](docs/lecture11-data-storage/starter-README.md). Evidence: [`docs/evidence/lecture11/evidence.md`](docs/evidence/lecture11/evidence.md).

Lecture 11 har sin egen PostgreSQL (`postgres-l11`, database `mobility_l11`, host-port 25411) og en S3-kompatibel object store (`object-store-l11`, host-port 19011), begge i compose-profilen `l11`. Konsolappen ligger i `src/MobilityTicketing.Reports*`. Vi arbejder kun i `src/MobilityTicketing.Reports.Application/ReportWorkflow.cs`, og fixtures i `fixtures/lecture11/` holdes uændrede.

Hjælpefunktionerne ligger i `lab-l11.sh` og kræver Bash (Git Bash på Windows). Kør alt fra repo-roden. I forhold til labbets tekst er der tre forskelle:
- Service-navnene er `postgres-l11` og `object-store-l11` i stedet for `postgres` og `object-store`.
- Fixtures ligger i `fixtures/lecture11/`, så det hedder fx `report publish R-001 fixtures/lecture11/validations-v1.jsonl`.
- `report()` er allerede defineret i `lab-l11.sh`. Den sætter `DOTNET_ROLL_FORWARD=Major`, så net8.0-appen også kører, når kun en nyere .NET-runtime er installeret.

Upload-kvitteringer skrives til `receipts/` i repo-roden. Mappen er gitignored.

### TODO
- [ ] Indlæs hjælpefunktionerne (i hver ny terminal): `source ./lab-l11.sh`.
- [ ] Start services: `lab_compose up -d --wait postgres-l11 object-store-l11`.
- [ ] Første gang, opret bucket med versioning: `s3 s3api create-bucket --bucket "$LAB_BUCKET"` og `s3 s3api put-bucket-versioning --bucket "$LAB_BUCKET" --versioning-configuration Status=Enabled`. Ved genoptagelse: `s3 s3api head-bucket --bucket "$LAB_BUCKET"`.
- [ ] Tjek: `s3 s3api get-bucket-versioning --bucket "$LAB_BUCKET"` (viser `"Status": "Enabled"`) og `show_reports`.
- [ ] Implementér `ReportWorkflow.cs` (lab afsnit 3-5).
- [ ] Kør eksperimenterne (afsnit 6) og udfyld evidence (afsnit 7).
- [ ] Stop: `lab_compose stop object-store-l11 postgres-l11`.
## Uge 9 – Transactions

Opgaven: [`docs/lecture9-transactions/lab.md`](docs/lecture9-transactions/lab.md). Starterens README: [`starter-README.md`](docs/lecture9-transactions/starter-README.md). Evidence: [`docs/evidence/lecture09/evidence.md`](docs/evidence/lecture09/evidence.md).

Lecture 9 har sin egen database (`postgres-l09`, database `mobility_l09`, schema `lab09`, ingen host-port). Vi arbejder i `database/postgres/lecture09/sql/book.sql` (endelig `lab09.book`) og gemmer hver variant i `database/postgres/lecture09/sql/variants/`. `run.sh`, `setup.sql` og de udleverede scripts (`unsafe-*.sql`, `inspect.sql`, `check.sql`) holdes uændrede.

Hvor lab.md skriver `docker compose exec postgres ...`, bruges `postgres-l09` i stedet. `sql/...` i lab.md svarer til `database/postgres/lecture09/sql/...`.

### TODO
- [ ] Start databasen: `docker compose up -d --wait postgres-l09`.
- [ ] Nulstil fixturen: `docker compose exec -T postgres-l09 bash /lab/run.sh reset` (indlæser også `book.sql`).
- [ ] Åbn session A og B (én pr. terminal): `docker compose exec postgres-l09 psql -X -U mobility -d mobility_l09 -v ON_ERROR_STOP=1 -v VERBOSITY=verbose`.
- [ ] Genindlæs funktionen: `docker compose exec -T postgres-l09 bash /lab/run.sh load`.
- [ ] Inspicér og tjek: `docker compose exec -T postgres-l09 bash /lab/run.sh inspect` og `... run.sh check`.

### Genskab en variant

Hver version af `lab09.book` ligger i `database/postgres/lecture09/sql/variants/` som en `CREATE OR REPLACE FUNCTION`. En tidligere version kan genskabes uden at ændre `book.sql`:

```powershell
docker compose exec -T postgres-l09 bash /lab/run.sh reset
docker compose exec -T postgres-l09 psql -X -U mobility -d mobility_l09 -f /lab/sql/variants/02-conditional.sql
```

`reset` giver frisk data og indlæser `book.sql`. Den anden linje erstatter funktionen i databasen med den valgte variant. Undtagelse: `01-unsafe-increment.sql` er et write-script til den usikre sekvens i opgave 1 og køres med `\i` i psql.

---

## Uge 10 – Distributed databases (MongoDB replica set)

Opgaven: [`docs/lecture10-distributed-databases/lab.md`](docs/lecture10-distributed-databases/lab.md). Starterens README: [`starter-README.md`](docs/lecture10-distributed-databases/starter-README.md). Evidence: [`docs/evidence/lecture10/evidence.md`](docs/evidence/lecture10/evidence.md) (rå output i `raw/`).

Lecture 10 er selvstændig og har sin egen `compose.yaml` i `database/mongodb/lecture10` (projekt `mobility-lecture10`, tre noder `mongo1`–`mongo3` plus en `client`, ingen host-porte). Vi arbejder kun i `scripts/verify.js`. Kør kommandoerne fra den mappe i Git Bash eller WSL. I Git Bash på Windows skal man først køre `export MSYS_NO_PATHCONV=1`, ellers omskriver Git Bash stierne `/lab/...`.

### TODO
- [ ] `cd database/mongodb/lecture10 && bash run.sh up && bash run.sh status`
- [ ] Opgave 1–4 med `bash run.sh write|read|verify|check` (se evidence)
- [ ] Stop/start noder: `docker compose stop mongo1` / `docker compose start mongo1`, derefter `bash run.sh up`
- [ ] Afslut med `bash run.sh down` (aldrig `-v`, medmindre data skal slettes)

---

Vores arbejde for de enkelte uger kan findes i `docs`, og PROGRESSION.md forklarer udviklingen.
