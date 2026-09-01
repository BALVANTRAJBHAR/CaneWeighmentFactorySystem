# ER DIAGRAM (text) — Core Relationships

```
ZONE (Id seq:1) ─────< VILLAGE (Id seq:101) ─────< GROWER (GrowerCode = VillageId/Seq)
                                                     │  BankId >──── BANK
                                                     │
                                                     ├────< PURCHASE (Id seq:1)
                                                     │        │ VehicleTypeId >── VEHICLE_TYPE
                                                     │        │ VarietyId >────── VARIETY >── VARIETY_TYPE ──< RATE (versioned)
                                                     │        │ SeasonId >─────── SEASON
                                                     │        └────< PURCHASE_IMAGE (Phase 6)
                                                     ├────< LOAN (Phase 8) ──< LOAN_RECOVERY
                                                     └────< PAYMENT (Phase 9, AdviceNumber seq:1)

USER ──< USER_ROLE >── ROLE ──< ROLE_PERMISSION >── PERMISSION (Module.Action)
USER ──< REFRESH_TOKEN (rotation/revocation)     USER ──< USER_OTP
AUDIT_LOG (every critical action, old/new values)

Config: WEIGHING_DEVICE >── STRING_PROFILE, DEVICE_CONFIG_HISTORY, WEIGHT_RULE_CONFIG,
        SOUND_CONFIG + SOUND_MESSAGE, CAMERA_CONFIG (1..6), PRINT_CONFIG,
        SMS_CONFIG + SMS_TEMPLATE, RAZORPAY_CONFIG, COMPANY_CONFIG, SYSTEM_SETTING,
        NUMBER_SEQUENCE (business serials)
```

## Key Indexes (SQL Server 2019 Express, Standard-compatible)
- Users.Username (unique), Permissions.Code (unique), RefreshTokens.TokenHash + UserId
- Zones.ZoneName (unique), Villages(ZoneId,VillageName) (unique)
- Growers: GrowerCode (unique), (VillageId,GrowerSequence) (unique), AadhaarHash (unique), Mobile
- Banks: (BankName,BranchName) unique, IFSC
- Varieties(VarietyTypeId,VarietyName) unique; Rates(VarietyTypeId,EffectiveFrom)
- Purchases: GrowerCode, VillageId, AdviceNumber, PaymentStatus, GrossTareStatus, GrossDateTime, LockStatus
- AuditLogs: Timestamp, (Module,Action); SystemSettings.Key unique

## Common master columns (soft delete everywhere)
`Id, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, Status, IsDeleted, DeletedAt, DeletedBy`

## Weight columns (decimal 12,2 — Quintal business unit; raw KG preserved)
`ScaleReadingGrossKg, GrossWeightQuintal, ScaleReadingTareKg, TareWeightQuintal, NetWeightQuintal,
CuttingPercent, CuttingWeightQuintal, TaxPercent, TaxWeightQuintal, FinalWeightQuintal, Rate,
PurchaseAmount (14,2)`
