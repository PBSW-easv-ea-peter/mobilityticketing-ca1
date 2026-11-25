
# Implementation lab: Can you reopen the original report?


A corrected validation export lands at the same S3 key as the original. Write the C# workflow that lets MobilityTicketing verify either report by ID and finish publishing a report after an interrupted write.
>Contents
>
>    1. Get the starter running
>    2. Trace the project boundaries
>    3. Stage an upload
>    4. Verify an exact version
>    5. Publish and recover
>    6. Run the experiments
>    7. Explain your design

## 1. Get the starter running
Clone the [Lecture 11 starter](https://github.com/patrickstolc/mobilityticketing-lecture-11-starter) and follow the README. You’ll need Docker and the .NET 8 SDK. Run commands from the repository root.

Implement `ReportWorkflow.cs` in `src/MobilityTicketing.Reports.Application`. Keep the fixtures unchanged.

## 2. Trace the project boundaries
`Program.cs` reads the input file and calls `ReportWorkflow`. **Application** defines the workflow and its interfaces in `Ports.cs`. **Infrastructure** implements those interfaces for S3, PostgreSQL and local receipts.

**Project references**

    CLI → Application
    CLI → Infrastructure → Application

| Component	| What it owns |
|-----------|--------------|
| PostgreSQL	| Operational validation records and the published report catalogue. Each report row holds its ID, predecessor, expected checksum and row count, plus the bucket, key and version that identify the export. The database enforces unique IDs and valid predecessor references. |
| Object storage |	The bytes of each generated export. The version ID lets two reports refer to different bytes at the same key. |
| Reporting service	| Upload and verify the exact version, then commit the catalogue row. On a read, resolve the report ID through the catalogue before opening the object. |
| Local receipt |	The version ID and expected measurements for an upload waiting to be published. |


>**When is a report published?**
>
>A report is published when its catalogue row commits. A successful PUT can leave a staged object behind if that commit fails.

Sketch the calls from the CLI to both stores. Mark the interface owners, adapter implementations, catalogue commit, and gap between upload and receipt.

## 3. Stage an upload

1. In `StageAsync`, compute the SHA-256 and number of newline characters from the supplied byte array. Those same bytes go to `IReportObjectStore.UploadAsync`.
2. Before uploading, call `IUploadReceipts.Reserve` for the report ID. The receipt adapter refuses an existing reservation. Reject a blank version ID, `null` or `None` returned by the upload.
3. Use `WriteAsync` to save a `ReportInput` with the report ID, destination, version and expected measurements. Return the version ID. An empty receipt means the upload outcome needs investigation before another PUT.

## 4. Verify an exact version

In `CheckAsync`, pass the stored bucket, key and version ID to `IReportObjectStore.ReadVersionAsync`. Measure the returned bytes and compare both values with the stored expectations. Return `Matches` or `Mismatch`. Translate `ObjectStoreException` to `ReadFailed`.

In `VerifyAsync`, look up the report with `IReportCatalogue.FindAsync`. Return `UnknownReport` for a missing row and `CatalogueReadFailed` for a `CatalogueException`. Otherwise call `CheckAsync`. The CLI uses the enum value as the exit code.

## 5. Publish and recover

Implement `RecoverAsync`. Read the staged receipt, verify its version using `CheckAsync`, and call `SaveAsync` only on `Matches`. If verification fails, throw an exception that reports the result. Return `Saved ID` once the catalogue write succeeds.
>Where to resume
>
>Implement PublishAsync by awaiting StageAsync and then returning the result of RecoverAsync. The separate stage command stops after saving the receipt so you can reproduce an interruption. Recovery uses the recorded version and can be repeated without issuing a PUT.

## 6. Run the experiments

Define this shorthand in the terminal where you sourced `lab.sh`.

```bash
report() { dotnet run --project src/MobilityTicketing.Reports -- "$@"; }
report publish R-001 fixtures/validations-v1.jsonl
report stage R-002 fixtures/validations-v2.jsonl
show_reports
s3 s3api list-object-versions --bucket "$LAB_BUCKET" --prefix "$LAB_KEY"
report verify R-002
```

1. Record both versions and explain why only the original report is in the catalogue.
2. Back up `receipts/R-002.json`. Change `Sha256` to 64 zeros, then run `report recover R-002 R-001`. Check the result and catalogue, then restore the backup.
3. Recover with a nonexistent predecessor ID to make the catalogue write fail. Then run the commands below. Compare the version list and catalogue before and after recovery.

```bash
report recover R-002 R-001
report recover R-002 R-001
report verify R-001
report verify R-002
show_reports
s3 s3api list-object-versions --bucket "$LAB_BUCKET" --prefix "$LAB_KEY"
```

4. Stop only the object store and verify the original. Record the result and exit code. Restart the store, wait for it to respond, and verify again. Explain why a failed read cannot tell you whether the bytes changed.
5. Stop PostgreSQL and run `report verify R-001` again. Compare its result with the object-store outage. Restart both services and record commands, outputs and exit codes in `docs/evidence.md`.

## 7. Explain your design

Answer these in `docs/evidence.md` using your results.

1. **Ownership**. Show where the original validation rows, export bytes, report metadata and pending upload state belong. Trace how an API receiving `R-001` finds the three-row export after the corrected four-row export becomes current.
2. **Safe retries**. For a failed PUT, a failed catalogue write with a receipt, and a crash before the receipt is written, state what might already exist, what the caller sees and what can safely be retried. Explain why an identical recovery can succeed twice. Try `report recover R-002` after successfully recovering it with `R-001` as predecessor. Record how the app handles that conflict and check the original catalogue row.
3. **Access and retention**. For an API version, decide where to check the caller’s right to read a report before resolving its object version. Describe which operations the app’s storage identity needs, and how a cleanup job would tell a pending upload from a version still referenced by a published report.