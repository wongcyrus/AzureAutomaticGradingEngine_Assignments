# Storage Schema Reference

| Table/container | Partition strategy | Purpose |
| --- | --- | --- |
| `SubscriptionRegistrations` | Shared `registrations` partition | Atomic email and subscription uniqueness indexes |
| `Classes` | Hashed teacher-owner partition | Teacher-owned class definitions |
| `ClassMemberships` | Class ID partition | Exact class roster |
| `GameStates` | Normalized student email | NPC state, reset marker, and active-task lock |
| `PassTests` | Sanitized student email | Current passing test records and marks |
| `FailTests` | Sanitized student email | Append-only failed-attempt history |
| `NPCCharacter` | Domain partition | NPC personality data |
| `EasterEgg` | Domain partition | Reward links |
| `PreGeneratedMessages` | Message type | Deterministic generated-message cache |
| `test-results` | Blob container | Private NUnit XML reports |

## Registration Indexes

```text
PartitionKey: registrations
RowKey: email:<SHA-256(normalized-email)>
RowKey: subscription:<normalized-guid>
```

Both rows contain normalized email, subscription ID, and index kind. They are
created and deleted in one transaction.

## Class Rows

```text
Classes
  PartitionKey: owner:<SHA-256(normalized-teacher-email)>
  RowKey:       <random class ID>

ClassMemberships
  PartitionKey: <class ID>
  RowKey:       student:<SHA-256(normalized-student-email)>
```

Class rows are reporting metadata only. Grading never reads them.

## Game-State Concurrency Rows

NPC state rows use `<game>-<npc>`. The fixed `__active_task_lock__` row and NPC
state share a student partition so assignment and completion can transact
atomically. `__reset_in_progress__` prevents writes during reset.

## Data Ownership

- Registration indexes select the subscription used for grading.
- Game/pass/failure rows are authoritative student progress.
- Class rows only authorize teacher reporting scope.
- Azure tags prove the first subscription claim but are not runtime indexes.

See [Technical design](../architecture/technical-design.md).
