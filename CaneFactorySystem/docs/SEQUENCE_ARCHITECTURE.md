# SEQUENCE ARCHITECTURE — Continuous Business Serial Numbers

## Requirement
Business-visible serials (PurchaseId, AdviceNumber, LoanId, PaymentId, LRId, SalePurchaseId,
GrowerSequence, ZoneId, VillageId) must:
- start from their business start value (1, or 101 for VillageId),
- stay continuous (1,2,3,4… — never jump like IDENTITY can after restarts/rollbacks),
- never be duplicated across concurrent operators.

**SQL Server IDENTITY is NOT used for these values** (IDENTITY caches values and creates permanent
gaps on rollback/restart).

## Design
Table `NumberSequences(Name NVARCHAR(64) PK, NextValue BIGINT)` — one row per sequence:
`ZoneId`, `VillageId` (start 101), `GrowerId`, `GrowerSeq:{villageId}` (per-village grower code),
`PurchaseId`, later `AdviceNumber`, `LoanId`, `PaymentId`, …

Reservation (`SequenceGenerator.NextAsync`) runs **inside the caller's DB transaction**:
```sql
UPDATE NumberSequences WITH (ROWLOCK, XLOCK, HOLDLOCK)
SET NextValue = NextValue + 1 WHERE Name = @name;   -- exclusive row lock until COMMIT
SELECT NextValue - 1 ...                             -- the reserved number
```
- The exclusive row lock serializes concurrent operators: the second transaction **waits** until
  the first commits or rolls back.
- **Commit** → number consumed, lock released, next caller gets N+1.
- **Rollback** → the UPDATE is undone, the same number is handed to the next caller →
  **no unexplained gaps**.
- A missing row is created atomically with the configured start value (first call returns exactly
  the start value: Zone → 1, Village → 101, Purchase → 1).

## Usage pattern (see `WeighmentController.Gross`)
```csharp
await using var tx = await db.Database.BeginTransactionAsync();
var purchaseId = (int)await sequences.NextAsync("PurchaseId", 1);
db.Purchases.Add(new Purchase { Id = purchaseId, ... });
await db.SaveChangesAsync();
await tx.CommitAsync();          // number is consumed only here
```

## Grower codes
`GrowerCode = "{VillageId}/{GrowerSequence}"` using per-village sequence rows
(`GrowerSeq:101` → 101/1, 101/2; `GrowerSeq:102` → 102/1). Uniqueness is additionally guaranteed
by unique indexes on `(VillageId, GrowerSequence)` and `GrowerCode`.

## Internal keys
Non-business tables (banks, items, tokens, audit…) use normal IDENTITY — allowed by the
specification for internal primary keys.
