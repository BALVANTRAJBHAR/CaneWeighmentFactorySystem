# CaneFactory database deletion and reference guide

## Rule zero: prefer business reversal or soft-delete

Production business rows should not normally be removed with SQL `DELETE`.
Purchases, payments, loans, recoveries, weighments, evidence and audit records form one financial/audit trail. Use the application's Cancel, Reverse, deactivate or soft-delete action whenever available. A hard delete should be reserved for a proven test/bad row, after a verified backup, during downtime, and inside a transaction.

Deleting a child row does **not** require deleting its masters. For example, deleting Purchase `Id=10` never requires deleting its Grower, Village, Vehicle Type, Variety or Season. Those are parents. Only rows that point **to Purchase 10** must be handled first.

## Current enforced foreign keys

`RESTRICT` means the parent cannot be physically deleted while a dependent row exists. `CASCADE` means SQL Server automatically deletes the listed dependent rows; this is convenient for technical join rows but dangerous for business masters.

| Parent | Referencing child | FK column | Delete rule |
|---|---|---|---|
| Banks | Growers | BankId | RESTRICT |
| ExpenseTypes | Expenses | ExpenseTypeId | RESTRICT |
| Growers | CashBookEntries | GrowerId | RESTRICT |
| Growers | Loans | GrowerId | RESTRICT |
| Growers | Payments | GrowerId | RESTRICT |
| Growers | Purchases | GrowerId | RESTRICT |
| Items | SaleItemRates | ItemId | RESTRICT |
| Items | SalePurchases | ItemId | RESTRICT |
| Loans | LoanRecoveries | LoanId | RESTRICT |
| LoanTypes | Loans | LoanTypeId | RESTRICT |
| Parties | SalePurchases | PartyId | RESTRICT |
| PaymentModes | Payments | PaymentModeId | RESTRICT |
| Payments | CashBookEntries | PaymentId | RESTRICT |
| Payments | LoanRecoveries | PaymentId | RESTRICT |
| Payments | PaymentPurchases | PaymentId | RESTRICT |
| Permissions | RolePermissions | PermissionId | CASCADE |
| Purchases | PaymentPurchases | PurchaseId | RESTRICT |
| Roles | RolePermissions | RoleId | CASCADE |
| Roles | UserRoles | RoleId | CASCADE |
| Seasons | Loans | SeasonId | RESTRICT |
| Seasons | Payments | SeasonId | RESTRICT |
| Seasons | Purchases | SeasonId | RESTRICT |
| StringProfiles | WeighingDevices | ActiveStringProfileId | RESTRICT |
| Users | RefreshTokens | UserId | CASCADE |
| Users | UserRoles | UserId | CASCADE |
| Varieties | Purchases | VarietyId | RESTRICT |
| VarietyTypes | Rates | VarietyTypeId | CASCADE |
| VarietyTypes | Varieties | VarietyTypeId | CASCADE |
| VehicleTypes | Purchases | VehicleTypeId | RESTRICT |
| VehicleTypes | SalePurchases | VehicleTypeId | RESTRICT |
| Villages | Growers | VillageId | CASCADE |
| Zones | Villages | ZoneId | CASCADE |

The main EF model currently contains 31 relationships; `SaleItemRates -> Items` is an additional migration-managed relationship, making 32 enforced relationships in the deployed SQL schema.

## Semantic references without an enforced FK

These columns still have to be considered even though SQL Server may not block a delete:

- `PurchaseImages`: PurchaseId, GrowerId, VillageId, CameraId and disk FilePath.
- `SalePurchaseImages`: SalePurchaseId, CameraId and disk FilePath.
- `PaymentImages`: PaymentId, optional PurchaseId, CameraId and disk FilePath.
- `SmsLogs`: optional GrowerId and PartyId plus business ReferenceId.
- `DeviceConfigHistories`: DeviceId.
- `UserOtps` and `AuditLogs`: optional UserId.
- CreatedBy, UpdatedBy, DeletedBy, GrossByUserId, TareByUserId, PaidByUserId and CapturedBy fields are historical scalar IDs, not enforced user FKs.
- Purchase also stores VillageId and VarietyTypeId snapshots without separate enforced FKs.

Physical deletion can therefore leave orphan image metadata/files or historical IDs even when SQL Server permits it.

## Safe process for any target row

1. Stop new entries for the affected module and take a fresh SQL backup.
2. Run `tools/database-delete-impact.sql` with the target table and ID. It only reports FKs and referencing-row counts.
3. Check semantic tables listed above and inspect AuditLogs/SmsLogs for history.
4. Prefer application Cancel/Reverse/soft-delete. Do not edit totals and status flags manually when a supported reversal exists.
5. If a hard delete is formally approved, write a row-specific script using `BEGIN TRANSACTION` and delete/reverse deepest children first.
6. Recalculate and validate balances/statuses before the parent delete.
7. Run reports and integrity checks. Use `ROLLBACK` while validating; change to `COMMIT` only after every check passes.
8. Record the reason, backup name, affected IDs, operator and before/after values in Audit Log/change records.

Never disable foreign keys globally and never use `NOCHECK` merely to make a delete succeed. That hides corruption instead of resolving it.

## Important deletion order examples

### Purchase

Preferred: cancel/correct the purchase before payment, or reverse its payment through the application.

For an approved hard cleanup, inspect in this order:

1. Find `PaymentPurchases` for PurchaseId.
2. If any exist, reverse the related Payment first; restore Purchase payment flags/advice through business logic.
3. Check `PaymentImages.PurchaseId`, then `PurchaseImages.PurchaseId` and their disk files.
4. Check related SMS/audit history.
5. Delete the Purchase only after dependent financial/evidence handling is complete.

Do not delete Grower, Village, VehicleType, Variety or Season merely to delete a Purchase.

### Payment

Preferred: use Payment Cancel/Reverse so purchase eligibility, loan balances and cash book remain consistent.

Dependency order is:

1. PaymentImages metadata/files (semantic reference).
2. CashBookEntries for PaymentId.
3. LoanRecoveries for PaymentId; reversing these must also restore Loan recovered/outstanding amounts.
4. PaymentPurchases; reversing these must restore every linked Purchase payment state/advice.
5. Payment.

Deleting only PaymentPurchases to bypass the FK will corrupt financial totals.

### Loan

Preferred: cancel/reverse from the Loan module.

1. Reverse/delete LoanRecoveries in business order.
2. If a recovery belongs to Payment, reverse that Payment first.
3. Verify RecoveredAmount and OutstandingAmount.
4. Delete/soft-delete Loan only after no LoanRecoveries remain.

### User

Use the application soft-delete/deactivate action. It revokes sessions while preserving usernames in historical records. A physical User delete cascades only RefreshTokens and UserRoles; historical `CreatedBy`, `PaidByUserId`, weighment operator IDs and AuditLogs remain as scalar values. The system Developer user must never be deleted.

### Master/configuration rows

- Grower: blocked by Purchases, Loans, Payments and CashBookEntries.
- Village/Zone: CASCADE can reach Growers, then fail on business RESTRICT rows. Soft-delete instead.
- Bank: blocked by Growers.
- VehicleType: blocked by Purchases and SalePurchases.
- VarietyType: cascades Rates and Varieties; a Variety used by Purchase blocks the operation.
- Item: blocked by SalePurchases and SaleItemRates.
- Party: blocked by SalePurchases.
- Season: blocked by Purchases, Loans and Payments.
- PaymentMode: blocked by Payments.
- LoanType: blocked by Loans.
- ExpenseType: blocked by Expenses.
- StringProfile: blocked while selected by a WeighingDevice.
- Role/Permission: join rows cascade, but seeded system roles/permissions must not be physically deleted.

For masters, normal practice is `Status = 0` or the application's soft-delete action, not physical deletion.
