# Implementation lab: Did the cancellation get through? (W46, Lecture 10)

Moodle: <https://moodle.easv.dk/mod/page/view.php?id=399918> · Starter: <https://github.com/patrickstolc/mobilityticketing-lecture-10-starter>

Short summary of the lab (see Moodle for the full text). We run a MongoDB 8.0 replica set with three
members and stop members to see what MobilityTicketing can still read, write and verify.
The test document is a cancellation notice `{_id: <request ID>, tripId: "T102", status: "Cancelled", sourceVersion: 2}`.
PostgreSQL still owns the real timetable and bookings. Use a new request ID for every experiment.

| Situation | What to establish |
| --- | --- |
| All three members running | Which member is primary, and what primary and secondary reads return |
| The primary stops | Whether the remaining members elect a replacement and requests can continue |
| `w:3` with only two members | Whether the timeout means the notice is absent |
| Only one member remains | Which reads and writes work after it recognises the loss of a majority |
| The set recovers | Whether each attempted request is present with the expected details |

1. **Read the cancellation from another member.** Predict the acks, write `BASE-MAJ` (majority) and `BASE-ONE` (1), read from primary and secondary.
2. **Stop the primary.** Read from a secondary and write `FAILOVER-01` right away, then `FAILOVER-02` once there is a new primary.
   With the member still stopped, run the acknowledgement timeout test (`ACK-ALL` with `w:3`, then read it). Restart the member.
3. **Lose the majority.** Keep the primary running and stop the other two. Wait until it is no longer PRIMARY. Predict, then run primary/secondary reads and the `w:1`/`majority` writes `NO-MAJ-ONE`/`NO-MAJ-MAJ`. Recover.
4. **Check what survived.** Implement `compareChange` in `scripts/verify.js` (`PresentMatches` / `PresentConflicts` / `AbsentAtVerification`), run `check`.
   Then write `RECOVERED-01` and verify every attempted ID plus the control `NEVER-SENT`.
5. **Save your evidence.** Get an AI second opinion on the comparison function and the ACK-ALL output, and try its suggestion.
   Fill in the decision table for journey search, ticket purchase and reporting.

Our answers: [`docs/evidence/lecture10/evidence.md`](../evidence/lecture10/evidence.md).
