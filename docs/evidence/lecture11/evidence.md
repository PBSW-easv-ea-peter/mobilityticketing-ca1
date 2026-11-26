# Cloud Storage: evidence

## Call sketch

```
CLI (MobilityTicketing.Reports)
 └─ Program.cs ── LocalReportServices (composition root, Infrastructure)
                   └─ skaber adapters og giver dem til ↓
Application: ReportWorkflow  ── bruger interfaces defineret i Ports.cs (Application ejer dem)
 │
 │ StageAsync
 ├─ IUploadReceipts.Reserve ───────▶ UploadReceipts      ▶ receipts/R-xxx.json (tom)
 ├─ IReportObjectStore.UploadAsync ▶ S3ReportObjectStore ▶ S3: ny version på samme key
 │        ┆  ◀── GAP: version findes i S3, men står ikke i nogen receipt ──▶
 ├─ IUploadReceipts.WriteAsync ────▶ UploadReceipts      ▶ receipts/R-xxx.json (version, sha, rows)
 │
 │ RecoverAsync
 ├─ IUploadReceipts.ReadAsync ─────▶ UploadReceipts      ▶ receipts/
 ├─ CheckAsync: ReadVersionAsync ──▶ S3ReportObjectStore ▶ S3: GET ?versionId=…
 └─ IReportCatalogue.SaveAsync ────▶ ReportCatalogue     ▶ PostgreSQL: INSERT reports  ★ PUBLICERET (commit)

 VerifyAsync:  FindAsync (PostgreSQL) → version-ID → ReadVersionAsync (S3)
```

Project references: CLI → Application, CLI → Infrastructure → Application. Application definerer og bruger interfaces; Infrastructure implementerer dem (dependency inversion), så workflowet aldrig kender S3 eller Npgsql.

Upload til S3 og commit i PostgreSQL er hver for sig atomiske, men intet koordinerer dem, så et nedbrud eller en netværksfejl imellem dem kan efterlade en version uden katalog-række. Designet forhindrer ikke den tilstand. Det gør den synlig (en receipt med version-ID) og genoprettelig (`recover` uden ny PUT), og oprydning sker som en separat proces.

| Scenario | Prediction | Command, result and exit status | What changed in the catalogue and bucket? |
| --- | --- | --- | --- |
| Publish the original | `Saved R-001`, én version, `supersedes` tom | `report publish R-001 fixtures/lecture11/validations-v1.jsonl` → `Saved R-001`, exit 0 | Bucket: 0 → 1 version (`742596b6…`, 306 B, IsLatest). Katalog: R-001, 3 rækker, supersedes NULL, samme version_id som i S3. |
| Interrupt after staging a correction | Stage udskriver versionId, én katalog-række, to versioner, verify `Matches` | `report stage R-002 fixtures/lecture11/validations-v2.jsonl` → `4a80ae2e-61c0-4311-ac65-feb448d87a5f`, exit 0. `report verify R-002` → `UnknownReport`, exit 3 | Bucket: 2 versioner på samme key, v2 (`4a80ae2e…`, 408 B) er IsLatest. Katalog uændret: kun R-001. `receipts/R-002.json` findes. |
| Recover with a mismatched receipt | `Mismatch`, exit 1, R-002 ikke i katalog, ingen ny version | Receipt Sha256 sat til 64 nuller. `report recover R-002 R-001` → `Mismatch`, exit 1. Backup gendannet bagefter. | Intet. Katalog: kun R-001. Bucket: stadig 2 versioner (v2 IsLatest), fordi recover aldrig laver PUT. `CheckAsync` stoppede før `SaveAsync`. |
| Recover with a nonexistent predecessor | FK-fejl, exit 1, receipt findes stadig | `report recover R-002 R-999` → `23503: … violates foreign key constraint "reports_supersedes_fkey"`, exit 1 | Intet. Katalog: kun R-001. Bucket: 2 versioner. `receipts/R-002.json` findes stadig, så versionen kan genoprettes. |
| Recover and repeat recovery | (ingen forudsigelse givet) | `report recover R-002 R-001` ×2 → `Saved R-002`, exit 0 begge gange | Katalog: R-002 tilføjet (4 rækker, supersedes R-001, version `4a80ae2e…`). Bucket: stadig 2 versioner, ingen PUT. Anden recover lykkes, fordi `ON CONFLICT DO NOTHING` indsætter 0 rækker, og en identisk række allerede findes, så transaktionen bare committer. Er rækken ikke identisk, kastes `ReportConflict`. |
| Verify the original after the correction | (ingen forudsigelse givet) | `report verify R-001` → `Matches`, exit 0. `report verify R-002` → `Matches`, exit 0 | Intet ændret. R-001 giver stadig `Matches`, fordi kataloget peger på version `742596b6…`, og CheckAsync læser netop den version, ikke den nyeste. Versioning bevarer v1, selvom v2 er skrevet til samme key. |
| Verify with object storage stopped | `ReadFailed`, exit 5. FindAsync lykkes, ReadVersionAsync fejler. `Matches` efter genstart | `lab_compose stop object-store-l11`, `report verify R-001` → `ReadFailed`, exit 5. Efter start og ventetid på head-bucket → `Matches`, exit 0 | Intet. En fejlet læsning siger intet om, hvorvidt bytes er ændret, fordi tjekket aldrig blev udført. Verify er read-only, så der er intet at rulle tilbage. Resultatet er ukendt og skal prøves igen, ikke tolkes som `Mismatch` eller `Matches`. |
| Verify with PostgreSQL stopped | exit 3 (`UnknownReport`) | `lab_compose stop postgres-l11`, `report verify R-001` → `CatalogueReadFailed`, exit 4. Efter start og ventetid på `SELECT 1` → `Matches`, exit 0 | Intet. S3 bliver aldrig kaldt, fordi FindAsync fejler først. `UnknownReport` er et bestemt svar ("rapporten findes ikke"), og hvis man brugte det ved en nedetid, kunne fx et oprydningsjob slette en publiceret version. Bestemte svar (0, 1, 3) må man handle på, fejlsvar (4, 5) betyder "prøv igen". |

## Version IDs and row counts

R-001 → `742596b6-bd1f-4eec-89d6-da6e9ade772b`, 3 rækker, sha256 `e94213959361fb09767d264c4afe218410c7e268f7ba865e7fcfcb4a75e19878`.
R-002 (staged) → `4a80ae2e-61c0-4311-ac65-feb448d87a5f`.

Kun R-001 står i kataloget, fordi R-002 kun er staged og ikke gemt i kataloget endnu. En rapport er først publiceret, når dens katalog-række committer. R-002 findes som version i S3 og i en lokal receipt, men `VerifyAsync` slår kun op i kataloget og svarer derfor `UnknownReport`.

## An additional check

## Storage decision

## Explain your design

### 1. Ownership

| Data | Ejer | Hvorfor |
| --- | --- | --- |
| Validation-rækker | Kildesystemet | Rapporten er en afledt eksport. Appen læser rækkerne, men ejer dem ikke (i laben: `fixtures/lecture11/`). |
| Eksportens bytes | S3 (versioned bucket) | Store, uforanderlige blobs. Versioning bevarer v1, når v2 skrives til samme key. |
| Rapport-metadata | PostgreSQL `reports` | Små felter, der skal slås op, beskyttes af constraints (FK på `supersedes`, CHECK på sha256) og committes atomisk. |
| Pending upload | Lokal receipt (`receipts/<id>.json`) | Kataloget har én betydning: findes rækken, er rapporten publiceret. Pending-rækker i kataloget ville tvinge alle læsere til at filtrere på status. |

Rækkefølgen er S3 først, PostgreSQL bagefter, så en katalog-række aldrig peger på bytes, der ikke findes.

**Opslag af R-001, efter at R-002 er blevet nyeste version:**
1. `FindAsync("R-001")` i PostgreSQL → `version_id 742596b6…`, sha256, 3 rækker.
2. `ReadVersionAsync(key, "742596b6…")` i S3 → v1's 306 bytes med 3 rækker.

En `GET` på key'en uden version-ID returnerer den nyeste version (v2, 4 rækker). Rapporten identificeres derfor via katalogets version-ID, ikke via S3-key'en.

### 2. Safe retries

| Fejl | Hvad findes allerede? | Hvad ser kalderen? | Hvad kan sikkert prøves igen? |
| --- | --- | --- | --- |
| PUT fejler | Tom receipt (reservation). I S3 måske intet, men ved timeout kan bytes være gemt som en forældreløs version. | Fejl fra `publish`. | `publish` igen, efter at den tomme receipt er fjernet. `Reserve` bruger `FileMode.CreateNew`, så to samtidige publish af samme rapport ikke kan overskrive hinanden. |
| Katalog-write fejler, receipt findes | Version i S3 og komplet receipt (version-ID, sha256, rækketal). Intet i kataloget, fordi transaktionen rullede tilbage. | Fejl fra `recover`/`publish` (fx `23503`, exit 1). | `recover`. Den laver ingen ny PUT, men verificerer receiptens version og gemmer. |
| Crash før receipt skrives | Tom receipt. Måske en version i S3, som intet binder til rapporten. Intet i kataloget. | Ingen svar (processen døde). | `publish` igen efter fjernelse af den tomme receipt. Det giver en ny version. Den gamle er forældreløs: S3 kan måle dens bytes, men kun receipten kender det forventede sha256, så den kan ikke verificeres. |

**Hvorfor en identisk recovery kan lykkes to gange:** `SaveAsync` bruger `ON CONFLICT DO NOTHING`. Anden gang indsættes 0 rækker, men den eksisterende række er identisk, så transaktionen committer og svarer `Saved R-002`. Recover er derfor idempotent.

**Konflikt:** `report recover R-002` (uden predecessor) → `ReportConflict`, exit 1. R-002's række er uændret: 4 rækker, supersedes `R-001`, version `4a80ae2e…`, og R-001 er også uændret. `ON CONFLICT DO NOTHING` indsatte 0 rækker, og da den eksisterende række har en anden `supersedes` end forsøget (NULL), afviser appen i stedet for at overskrive. En retry må kun lykkes, hvis den er identisk med det, der allerede er gemt.

### 3. Access and retention

**Adgang:** Rettighedstjekket ligger før eller i katalog-opslaget (`FindAsync`), så version-ID'et aldrig udleveres til en bruger uden adgang. Version-ID'et er nøglen til bytes, så selv metadata må ikke lække. Manglende adgang og ukendt rapport giver samme svar, så man ikke kan gætte, hvilke rapporter der findes.

**Storage-identitet:** Appen skal kun bruge `PutObject` (`StageAsync`) og `GetObject` med versionId (`CheckAsync`). Den skal ikke kunne slette. Sletning hører til en separat cleanup-identitet, så en kompromitteret app ikke kan fjerne publicerede versioner.

**Cleanup:** Jobbet lister versioner i S3 og slår hver op i kataloget og i receipts:
- I kataloget → publiceret, behold.
- I en komplet receipt → pending, behold (`recover` kan stadig køre).
- Ingen af dem og ældre end en grace-periode → forældreløs, må slettes med `DeleteObject` + versionId.

Grace-perioden beskytter en upload, hvor PUT lige er lykkedes, men receipten endnu ikke er skrevet. Fejler katalog-opslaget (`CatalogueReadFailed`), sletter jobbet ingenting. Et fejlsvar er ikke det samme som "ikke refereret" (jf. eksperiment 5).
