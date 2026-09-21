# Weighment Corrections (Admin / Developer)

Open **Weighment Corrections** from the sidebar, choose Cane Purchase or Sale / Purchase, enter the ID and click Search. Change the details, click Calculate / Preview, then Update and Confirm Update.

## Cane purchase

- Editable: vehicle type, vehicle number, variety type and variety. The variety must belong to the selected type and the selected masters must be active.
- Changing variety type uses the active Rate Master effective on the **original gross date**, matching the original rate-snapshot rule. It does not use a future rate. If no applicable rate exists, configure Rate Master first; the update is rejected.
- Amount = existing final weight in quintals × the newly selected type's rate, rounded to two decimals. Gross/tare/final weights, cutting and tax deductions are not modified.
- A vehicle-only or same-variety-type edit preserves the saved rate and amount. A pending gross may be corrected but has no final amount until tare is completed.
- Paid purchases are rejected at lookup, preview and update. The server checks PaymentFlag, PaymentStatus and completed PaymentPurchase links, not just the screen. Cancelled/inactive/locked purchases cannot be corrected.
- Vehicle numbers are normalized, and a pending record cannot be changed to a vehicle that already has another pending record in the same module.

## Sale / Purchase limitation

The existing non-cane SalePurchase model uses Item and Party, **not cane varieties**. It has no link to Payment/PaymentPurchase. This form corrects only vehicle type and vehicle number there; item, party, rate, weight and amount remain unchanged. It cannot claim to check an external/unlinked sale payment. Adding sale payment tracking and variety-based sale pricing is a separate data-model/business-workflow change.

## Authorization, history and simultaneous users

- Both the form and API require Admin or Developer. Operator Purchase.Edit permission alone is insufficient.
- Existing UpdatedBy/UpdatedAt columns record the user ID and UTC timestamp; the form displays the user name and local time. Audit Log stores old/new details, user name and timestamp atomically with the correction.
- If another user changes/pays/finalizes the record after it was loaded, the old correction is rejected; search again. Rate changes after preview also require another preview.
- Existing purchase/sale UpdatedAt columns are optimistic concurrency tokens (purchase payment flags also participate). A single/farmer/date-range payment loaded before a correction cannot commit its stale amount. Its database transaction, cash entry, loan recoveries and sequence reservations roll back on conflict.
- These are EF model metadata changes using existing columns; no new database columns or migration are required.

## Verification / deployment

From `C:\Balvant\Flutter\CaneFactorySystem\CaneFactorySystem`:

```powershell
dotnet run --project backend\tests\WeighmentCorrectionRegression\WeighmentCorrectionRegression.csproj
dotnet build backend\src\CaneFactory.API\CaneFactory.API.csproj -c Release
```

From `C:\Balvant\Flutter\CaneFactorySystem\CaneFactorySystem\frontend\cane_factory_app`:

```powershell
flutter test test\weighment_corrections_test.dart
flutter analyze lib\screens\weighment\weighment_corrections_screen.dart lib\widgets\app_shell.dart
```

The regression runner uses an isolated in-memory SQLite database, not production SQL Server. It covers paid/stale rejection, attribution, calculations, unchanged weights, audit rollback and the actual single/farmer/date-range payment controller paths. It does not start hardware or send SMS.

Deploy both the rebuilt API and Windows client to expose the new form and enforce the new checks. Source changes alone do not update an already published installation. No production database was changed by these tests.
