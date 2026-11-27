# CA2 review guide – MobilityTicketing

Compulsory Assignment 2 i Databases for Developers samler fem ugelabs: NoSQL (L5), key-value (L6), database design optimization (L7), query optimization (L8) og transactions (L9). Guiden viser, hvor arbejdet og evidensen ligger, hvordan man kører det, og hvilke beslutninger vi gerne vil diskutere.

## Afleveret commit

- Repo: <https://github.com/PBSW-easv-ea-peter/mobilityticketing-ca1>
- Commit: det hash, der står i Moodle-afleveringen. Koden er testet på `a3bec56`. Commits derefter ændrer kun dokumentation.
- Kørt fra et frisk clone af `a3bec56` den 27/11-2026: alle fem labs grønne, se [Testkørsel](#testkørsel).

## Setup

Krav: Docker Desktop (Compose v2) og .NET 10 SDK (kun til Redis-labbet i L6, som kører på værten). Alle kommandoer køres fra repo-roden i PowerShell eller bash. I Git Bash på Windows skal man først køre `export MSYS_NO_PATHCONV=1`, ellers bliver `/scripts/...` lavet om til en Windows-sti.

Hver lab har sin egen database eller container, så de kan køres i vilkårlig rækkefølge og nulstilles hver for sig.

| Lab | Container | Database |
| --- | --- | --- |
| L5 NoSQL | `mongo` | `mobility.journey_search` |
| L6 Redis | `redis` + `mongo` | ingen persistens i Redis |
| L7 route overview | `postgres-l07` + `app` / `tests` | `mobility_l07`, schema `lab07` |
| L8 query optimization | `postgres-l08` | `mobility_l08`, schema `lab08` |
| L9 transactions | `postgres-l09` | `mobility_l09`, schema `lab09` |

Alt kan fjernes igen med `docker compose down -v`, som også sletter de navngivne volumes.

## Emnerne

### L5 · NoSQL: journey-search dokumentmodel

Start og nulstil:

```
docker compose up -d --wait mongo
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/setup.js
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/reset.js
```

Kør søgning, validering, ændringer og vækst. Kør `reset.js` før hvert script, fordi `updates.js` og `alternative.js` ændrer data:

```
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/search.js
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/validation.js
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/updates.js
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/growth.js
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/alternative.js
```

- Kode: [`database/mongodb/lecture05/`](../database/mongodb/lecture05/)
- Evidens og forklaring: [`docs/lecture5-no-sql_databases/dossier.md`](lecture5-no-sql_databases/dossier.md), output i [`search-results.txt`](lecture5-no-sql_databases/search-results.txt)
- **Document boundary:** `journey_search` har ét dokument pr. by, rute og stop-par med en array af departures, fordi søgningen altid er "fra stop A til stop B i et tidsvindue". Dossierets afsnit 5 viser, at dokumentet vokser ca. 123 bytes pr. departure og skrives om ved hver ændring. Afsnit 6 bygger derfor `journey_search_by_trip` med én dateret departure pr. dokument (`alternative.js`). Den klarer de samme 10 søgetests med én `find`, og det er den model, vi ville fortsætte med.
- **En ændring:** aflysning af `LAB05-T-OK`, som findes i to dokumenter. `updateMany` med `arrayFilters` rammer alle kopier og er idempotent. Aflyses kun den ene kopi, giver to søgninger forskellige svar (dossier afsnit 4).

### L6 · Redis-cache på journey search

Start og nulstil:

```
docker compose up -d --wait mongo redis
docker compose exec -T mongo mongosh --quiet --file /scripts/lecture05/reset.js
docker compose exec -T redis redis-cli FLUSHDB
```

Kør cache-testene, aflysning med en cachet søgning og ti samtidige søgninger:

```
dotnet run --project database/redis/lecture06/CacheLab
dotnet run --project database/redis/lecture06/CacheLab -- morning
dotnet run --project database/redis/lecture06/CacheLab -- concurrent
```

Redis nede: `docker compose stop redis`, kør igen, og `docker compose start redis` bagefter.

- Kode: [`database/redis/lecture06/CacheLab/`](../database/redis/lecture06/CacheLab/) (`CachedJourneySearch.cs`, `CacheKeys.cs`), Lua-script [`decrement_if_positive.lua`](../database/redis/lecture06/decrement_if_positive.lua)
- Evidens og forklaring: [`docs/lecture6-caching-journey-search-with-redis/dossier.md`](lecture6-caching-journey-search-with-redis/dossier.md)
- **Når kildedata ændres:** en aflyst afgang bliver vist fra cachen, indtil nøglen slettes eller udløber (dossier afsnit 5, trin 6). Efter `DEL` læses MongoDB igen, og afgangen er væk.
- **Freshness:** cache-aside med TTL på 20 s som øvre grænse og `DEL` som invalidation. Nøglen indeholder alle input, der kan ændre resultatet, plus en formatversion (`v2`).
- **Failure:** Redis nede → søgningen går direkte til MongoDB. `BacklogPolicy.FailFast` tog den fra 12 s til ca. 120 ms. MongoDB nede → fejlen vises, i stedet for at passageren får en tom liste.

### L7 · Route overview

Start, byg og nulstil:

```
docker compose --profile l07 up -d --wait postgres-l07
docker compose --profile l07 build app tests
docker compose --profile l07 run --rm app reset
```

Mål og test:

```
docker compose --profile l07 run --rm app baseline
docker compose --profile l07 run --rm app overview
docker compose --profile l07 run --rm app scale
docker compose --profile l07 run --rm tests
```

- Kode: [`src/MobilityTicketing.Lecture07/`](../src/MobilityTicketing.Lecture07/) (`StudentApplication.cs`, `Queries/LiveRouteOverviewQuery.cs`, `Queries/PublishedPlanQuery.cs`), tests i [`tests/MobilityTicketing.Lecture07.Tests/StudentTests.cs`](../tests/MobilityTicketing.Lecture07.Tests/StudentTests.cs)
- Design: [`docs/lecture7-route-overview/design.md`](lecture7-route-overview/design.md)
- Målinger: [`docs/evidence/lecture07/README.md`](evidence/lecture07/README.md)
- **Access pattern:** live-tavlen spørger om vilkårlige tidsvinduer på op til 24 timer. Derfor beregnes den ved hvert kald (Design A) i stedet for at blive læst fra en summary-tabel med faste buckets (Design B).
- **Målinger:** ved 200 ruter og 100.000 trips laver den lazy baseline 201 kald og materialiserer 100.200 entities. Live-tavlen laver 1 kald og materialiserer 0 entities.
- **Trade-off:** live-tavlen koster en aggregering pr. kald, men aflysning og omdøbning skal kun opdatere én række. En publiceret plan gemmer kopierede værdier, så senere ændringer ikke rører den.

### L8 · Query optimization: departure search

Start, nulstil og tjek:

```
docker compose up -d --wait postgres-l08
docker compose exec -T postgres-l08 bash /lab/run.sh reset
docker compose exec -T postgres-l08 bash /lab/run.sh check
```

Opret vores index, mål baseline og vores version, og vis partitioning. `reset` fjerner indexet, så `indexes` skal køres før `measure`:

```
docker compose exec -T postgres-l08 bash /lab/run.sh indexes
docker compose exec -T postgres-l08 bash /lab/run.sh measure baseline R-0
docker compose exec -T postgres-l08 bash /lab/run.sh measure candidate R-0
docker compose exec -T postgres-l08 bash /lab/run.sh partitions
```

- Kode: [`database/postgres/lecture08/queries/candidate.sql`](../database/postgres/lecture08/queries/candidate.sql), [`indexes.sql`](../database/postgres/lecture08/queries/indexes.sql), [`partitioning.sql`](../database/postgres/lecture08/queries/partitioning.sql)
- Evidens: [`docs/evidence/lecture08/evidence.md`](evidence/lecture08/evidence.md) og de rå plans i [`docs/evidence/lecture08/`](evidence/lecture08/)
- **Testede ændringer:** (1) rewrite til et halvåbent UTC-interval i stedet for `AT TIME ZONE … ::date`, (2) index kun på `route_id`, (3) partielt index `(route_id, scheduled_departure_utc, id) WHERE status = 'Scheduled'`.
- **Vi beholder** rewrite + det partielle index. Route-only indexet fjernes, se beslutning 1.

### L9 · Transactions: den sidste plads

Start, nulstil og tjek:

```
docker compose up -d --wait postgres-l09
docker compose exec -T postgres-l09 bash /lab/run.sh reset
docker compose exec -T postgres-l09 bash /lab/run.sh check
```

Konkurrerende requests køres i to terminaler (session A og B) med:

```
docker compose exec postgres-l09 psql -X -U mobility -d mobility_l09 -v ON_ERROR_STOP=1 -v VERBOSITY=verbose
```

En tidligere variant genskabes med `reset` efterfulgt af `psql -f /lab/sql/variants/<fil>.sql` (se README, Uge 9).

- Kode: endelig funktion i [`database/postgres/lecture09/sql/book.sql`](../database/postgres/lecture09/sql/book.sql), varianter i [`sql/variants/`](../database/postgres/lecture09/sql/variants/)
- Evidens: [`docs/evidence/lecture09/evidence.md`](evidence/lecture09/evidence.md) (30 forsøg i én tabel: rækkefølge, hvem venter på hvad, SQLSTATE og slut-tal)
- **Konkurrerende requests:** uden beskyttelse bliver den sidste plads solgt to gange (forsøg 1: 2 bookinger, `reserved_seats = 1`). Med `book()` venter B på A's række-lås og bliver enten afvist (A committer, forsøg 3) eller får pladsen (A ruller tilbage, forsøg 4).
- **Invarianten** (`reserved_seats <= capacity` og én booking pr. reserveret plads) beskyttes af én betinget `UPDATE … WHERE reserved_seats < capacity`. `request_id` er unik, så et gentaget request returnerer den oprindelige booking uden at tage en plads til (forsøg 23–27).

## To beslutninger

### 1. Partielt sammensat index til departure search (L8)

- **Valg:** `(route_id, scheduled_departure_utc, id) WHERE status = 'Scheduled'` sammen med et halvåbent UTC-interval.
- **Alternativ:** index kun på `route_id` (832 kB mod 4408 kB).
- **Hvorfor det passer til MobilityTicketing:** passagerer søger altid én rute, én dag og vil se de første 20 afgange i rækkefølge. Indexet leverer rækkerne i den rækkefølge, så `LIMIT 20` kan stoppe scanningen tidligt. Aflyste afgange søges aldrig og er derfor ikke med i indexet.
- **Evidens:** for R-0 (75 % af tabellen) gik det fra 7,507 ms og 1007 buffers til 0,124 ms og 24 buffers, uden Sort-node. Route-only indexet blev ignoreret af planneren for R-0, og for R-7 læste det stadig alle 1001 sider ([evidence.md](evidence/lecture08/evidence.md), "Keep or remove?").
- **Pris:** omlægning og aflysning ændrer kolonner i indexet, så de kan ikke være HOT updates. Det giver mere write-I/O, WAL og bloat.

### 2. Betinget UPDATE med idempotent request-id (L9)

- **Valg:** `INSERT … ON CONFLICT (request_id) DO NOTHING` og derefter `UPDATE trips SET reserved_seats = reserved_seats + 1 WHERE … AND reserved_seats < capacity` under Read Committed. Fejler opdateringen, rulles begge dele tilbage.
- **Alternativ:** `SELECT … FOR UPDATE` før skrivningen, eller Serializable med retry på `40001`.
- **Hvorfor det passer til MobilityTicketing:** check og skrivning sker i én sætning, så række-låsen holdes kortest muligt, og bookinger på andre trips venter ikke (forsøg 7). Appen behøver ingen retry-løkke. Et dobbeltklik eller en retry fra en mobil med dårlig forbindelse giver ikke en ekstra plads.
- **Evidens** (forsøgsnumre i [evidence.md](evidence/lecture09/evidence.md)): 1 (uden beskyttelse: dobbeltbooking), 3–4 (betinget update: én booking, tallene passer), 10 (table lock: andre trips venter unødigt), 19–22 (isolation levels: korrekt, men kræver retry af hele transaktionen), 24–27 (samme request to gange og genbrug af request-id).

## Begrænsning og næste tjek

**Et gammelt resultat kan komme tilbage i cachen (L6).** Hvis en søgning læser MongoDB lige før en aflysning og skriver til Redis lige efter `DEL`, ligger den aflyste afgang i cachen igen (dossier afsnit 5, "Race"). Cache-aside ved ikke, hvilken version af data den læste, så kun TTL'en på 20 s begrænser fejlen.

**Næste tjek:** gem en versions-tæller pr. rute, som øges ved hver ændring, og læg den i værdien. Gentag racet og vis, at en skrivning med en ældre version afvises (fx med et Lua-script, der sammenligner versionen før `SET`).

Andre åbne spørgsmål: L9 er kun testet med to samtidige sessioner.

## Testkørsel

Frisk `git clone` af `master` på `a3bec56`, 27/11-2026, Windows 11 med Docker Desktop og Git Bash (`MSYS_NO_PATHCONV=1`). Kommandoerne er dem, der står i denne guide.

| Lab | Kommando | Resultat |
| --- | --- | --- |
| – | `docker compose config` | Gyldig |
| L5 | `search.js`, `validation.js`, `alternative.js` (efter `reset.js`) | 10/10 PASS. Validering afviser 2/2 (code 121). `alternative.js` 10/10 PASS før aflysning |
| L6 | `dotnet run --project database/redis/lecture06/CacheLab`, derefter `-- morning` med Redis stoppet | 12 PASS, 0 FAIL. Redis nede: `cache unavailable`, svar fra MongoDB |
| L7 | `docker compose --profile l07 run --rm tests`, `scale` + `overview` | 51/51 tests. Live-tavlen: 1 read-kommando ved 200 ruter / 100.000 trips |
| L8 | `run.sh check`, `indexes`, `measure candidate R-0` | 4/4 PASS. Index Scan på `trips_route_sched_dep_id_idx`, 0,24 ms |
| L9 | `run.sh reset` + `run.sh check` | "Single-session checks passed" |
