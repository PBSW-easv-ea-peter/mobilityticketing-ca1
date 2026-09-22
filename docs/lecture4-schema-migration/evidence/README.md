# Lecture 4 evidence

TODO — record here (per `docs/lecture4-schema-migration/lab.md`):

- SQL, commands and results for each migration stage (compatible overlap,
  backfill, old writer still working, checks before removing the old
  reference).
- The before/after table of which inserts and queries work at each stage.
- Proof that the three existing tickets keep their original prices and
  currencies throughout.
- The rollout decision: when the old writers would be stopped, and whether
  rollback to the old application version would still be possible.
