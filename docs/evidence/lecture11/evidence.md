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
| Publish the original | | | |
| Interrupt after staging a correction | | | |
| Recover with a mismatched receipt | | | |
| Recover with a nonexistent predecessor | | | |
| Recover and repeat recovery | | | |
| Verify the original after the correction | | | |
| Verify with object storage stopped | | | |

## Version IDs and row counts

## An additional check

## Storage decision
