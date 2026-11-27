# Lecture 10 evidence

Run on 2026-11-25 against the `mobility-lecture10` replica set (`rs0`, MongoDB 8.0,
three voting members). Full command output for every step is in `raw/`, named
`<task>-<step>-<what>.txt`. Each file starts with the command and ends with its exit code.

## Experiments

| Member state | Action | Operation ID | Read/write settings | Prediction | Actual result / saved output |
| --- | --- | --- | --- | --- | --- |
| mongo1 PRIMARY, mongo2+3 SECONDARY | write | BASE-MAJ | `w:majority`, wtimeout 2000 | Needs 2 of 3 acks (primary + 1 secondary). Acknowledged. | `Acknowledged`, n=1, 382 ms. `raw/1-1-write-base-maj.txt` |
| same | write | BASE-ONE | `w:1` | Primary only. Acknowledged, slightly faster. | `Acknowledged`, n=1, 338 ms (not measurably faster on one host). `raw/1-2-write-base-one.txt` |
| same | read | BASE-MAJ | primary, `local` | Present | `Present`, all four fields. `raw/1-3-read-primary.txt` |
| same | read | BASE-MAJ | secondary, `local` | Present (majority write means at least one secondary has it) | `Present`, identical document. `raw/1-4-read-secondary.txt` |
| mongo1 stopped (`docker compose stop mongo1`) | read | BASE-MAJ | secondary | Present, secondaries keep serving reads | `Present`, 537 ms. `raw/2-1-read-secondary.txt` |
| mongo1 stopped, election in progress | write | FAILOVER-01 | `w:majority` | Might fail with server-selection timeout until a new primary is elected | `Acknowledged`, 491 ms, `electionId …02`, term 2. Graceful shutdown handed over before the write. `raw/2-2-write-failover-01.txt` |
| mongo2 PRIMARY, mongo3 SECONDARY, mongo1 unreachable | status | – | direct to mongo2 | New primary elected | mongo2 `PRIMARY`, mongo3 `SECONDARY`, mongo1 `(not reachable/healthy)`. `raw/2-3-status-mongo2.txt` |
| same | write | FAILOVER-02 | `w:majority` | Acknowledged (2 of 3 still a majority) | `Acknowledged`, 316 ms, term 2. `raw/2-4-write-failover-02.txt` |
| same | write | ACK-ALL | `w:3`, wtimeout 2000 | Times out: only 2 members can ack. Document still inserted on the primary. | `UnknownWriteOutcome`, code 64 `WriteConcernFailed` "waiting for replication timed out", **but `n:1, ok:1`**, 2354 ms. `raw/2-5-write-ack-all.txt` |
| same | read | ACK-ALL | primary, `local` | Present | `Present`, all four fields. `raw/2-6-read-ack-all.txt` |
| mongo1 restarted | status | – | – | Back to 1 primary + 2 secondaries | mongo2 PRIMARY, mongo1 + mongo3 SECONDARY. `raw/2-9-status.txt` |
| mongo1 + mongo3 stopped, mongo2 alone | status (polled) | – | direct to mongo2 | mongo2 steps down once it cannot see a majority | `PRIMARY`, `PRIMARY`, then `SECONDARY` (~10 s). `raw/3-1-status-mongo2.txt` |
| mongo2 SECONDARY, no primary | read | BASE-MAJ | primary | Fails, no primary to select | `UnknownReadFailure` after 3237 ms (server-selection timeout 3000 ms). Message `getaddrinfo EAI_AGAIN mongo1` is just the last error seen while looking for a primary. `raw/3-2-read-primary.txt` |
| same | read | BASE-MAJ | secondary | Present, a secondary can still serve | `Present`, 286 ms. `raw/3-3-read-secondary.txt` |
| same | write | NO-MAJ-ONE | `w:1` | Fails, `w` cannot help without a writable primary | `UnknownWriteOutcome` after 3261 ms, same server-selection error. `raw/3-4-write-nomaj-one.txt` |
| same | write | NO-MAJ-MAJ | `w:majority` | Fails the same way | `UnknownWriteOutcome` after 3257 ms. `raw/3-5-write-nomaj-maj.txt` |
| same | verify | BASE-MAJ | primary, `majority` | `UnknownReadFailure`, not "absent" | `UnknownReadFailure`, exit 1. `raw/3-6-verify-no-primary.txt` |

`w` is the number of acknowledgements requested **after** a primary has accepted the
write. With no primary, the client never gets as far as sending the insert, so
`w:1` and `w:majority` fail identically (server selection).

## Read-only verification

`compareChange` implemented in `database/mongodb/lecture10/scripts/verify.js`.
`bash run.sh check`: 3/7 after the first step (null + `_id`), 7/7 after comparing all four fields
(`raw/4-1-check-step1.txt`, `raw/4-2-check-final.txt`).

Verified after full recovery (mongo2 PRIMARY, term 3), `raw/4-4-verify-all.txt`:

| Operation ID | Earlier write outcome | Verification result | What this establishes / leaves uncertain |
| --- | --- | --- | --- |
| BASE-MAJ | Acknowledged (majority) | PresentMatches | Majority-committed and survived two failovers. |
| BASE-ONE | Acknowledged (w:1) | PresentMatches | Survived here, but `w:1` alone did not guarantee it. See the rollback experiment below. |
| FAILOVER-01 | Acknowledged (majority) during the handover | PresentMatches | The handover finished before the write. |
| FAILOVER-02 | Acknowledged (majority) | PresentMatches | Writes continue on the new primary. |
| ACK-ALL | **UnknownWriteOutcome** (w:3 timeout) | **PresentMatches** | The error and the stored notice are both real. The timeout only meant "3 acks not received in 2 s". |
| NO-MAJ-ONE | UnknownWriteOutcome (no primary) | AbsentAtVerification | Expected, because the insert never reached a primary. Still only "absent at this read". |
| NO-MAJ-MAJ | UnknownWriteOutcome (no primary) | AbsentAtVerification | Same as above. |
| RECOVERED-01 | Acknowledged (majority), term 3 | PresentMatches | The set accepts majority writes again after recovery. |
| NEVER-SENT | never written (control) | AbsentAtVerification | The control. ACK-ALL looked like a failure but is present. NEVER-SENT is truly absent. |

## Application policy

| Operation | Freshness/consistency needed | During an outage | What the user sees | After recovery | Reason / evidence |
| --- | --- | --- | --- | --- | --- |
| Journey search | Seconds of staleness acceptable. It is a copy; PostgreSQL owns the timetable. | Keep serving from any reachable member (`secondaryPreferred`). | Results plus "Live changes may be delayed (last update hh:mm)". Cancellations are flagged as "check before travelling". | Switch back automatically; no user action. | In task 3 the secondary read of BASE-MAJ worked with no primary, while the primary read failed after 3.2 s. |
| Ticket purchase | Authoritative and confirmed. Must not double-book or sell a cancelled trip. | Do not confirm purchases. Stop at "pending" with the request ID. Never treat a timeout as "failed". | "We could not confirm your purchase yet. You have not been charged twice; we will notify you." | Verify the request ID with a majority read before any retry (idempotent). Only then complete or reissue. | ACK-ALL returned `WriteConcernFailed` yet was `PresentMatches`. ROLLBACK-ONE was `Acknowledged` (w:1) yet was rolled back. Only majority-verified state is safe. |
| Reporting | Complete and consistent, but not urgent. | Pause or queue report jobs. Never produce a report from a partial or failed read. | "Report delayed: data source unavailable. Last complete report: date." | Re-run with `readConcern: majority` once a primary exists. | During no-primary, verify returned `UnknownReadFailure`. Treating that as "0 rows" would silently under-report. |

## AI second opinion

- Prompt and model: Claude (a separate agent, no prior context). It was given the `compareChange` source,
  the ACK-ALL write and read output, and the verification result. It was asked to suggest one
  missing check and challenge the interpretation.
- Suggested claim or experiment: "Your data fits both 'will commit' and 'may roll back'."
  Isolate the primary, write to it, then elect a new primary without it and let it rejoin.
  A timed-out (or even acknowledged `w:1`) write may then be rolled back. It also flagged that
  a local read on the primary only proves local presence, not durability. Minor suggestions:
  extra fields are not checked, and a duplicate-key error on retry should lead to a verify.
- My prediction: both writes are visible locally, then rolled back when mongo2 rejoins and the
  verify returns `AbsentAtVerification`.
- Actual output (`raw/5-*`): with mongo1 and mongo3 stopped, mongo2 (still PRIMARY for a few
  seconds) returned `Acknowledged` for **ROLLBACK-ONE (`w:1`)** and `WriteConcernFailed` (n:1)
  for ROLLBACK-MAJ (`w:majority`). A secondary read of ROLLBACK-ONE on mongo2 returned `Present`.
  Then mongo2 was stopped and mongo1 and mongo3 were started, and mongo3 became PRIMARY.
  Verify returned `AbsentAtVerification` for both IDs. When mongo2 restarted, its log showed
  `Rollback summary` with `"insert":2` reverted. `bsondump` of
  `/data/db/rollback/…/removed.2026-11-25T08-41-24.0.bson` contains exactly ROLLBACK-ONE and ROLLBACK-MAJ.
- Decision and remaining uncertainty: I changed the interpretation. A `w:1` acknowledgement
  or a write concern timeout is **not** a durability signal. Only a majority-acknowledged write or
  a majority read can be relied on. I kept `compareChange` strict on the four business fields and
  did not reject extra fields: they do not change the meaning of the notice, and the README defines
  a conflict as a difference in those fields. Remaining uncertainty: all members run on one host,
  so this does not show behaviour under real network partitions or latency.
