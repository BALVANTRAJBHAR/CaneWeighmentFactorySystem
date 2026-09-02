# ROLE / PERMISSION MATRIX (seeded defaults — adjustable at runtime via Roles screen)

Permission format: `Module.Action`. Actions: View, Create, Edit, Delete, Approve, Lock, Unlock,
Cancel, Reverse, Pay, Export, Print, Configure, ViewCamera, ViewSensitiveData (570 total seeded).

| Capability | Developer | Admin | SubAdmin | Accountant | Operator | Farmer | SalePurchase |
|---|---|---|---|---|---|---|---|
| All permissions | ✅ (all 570) | — | — | — | — | — | — |
| Masters (Zone…PaymentMode) | ✅ full | ✅ View/Create/Edit/Delete/Export/Print | ✅ View/Create/Edit | View (Grower/Village/Bank) | View (weighment-related) | — | View (Party/Item/Vehicle) + Create (Party/Vehicle) |
| Grower management | ✅ | ✅ | ✅ | View | View | — | — |
| Rate Master + unpaid recalculation (Approve) | ✅ | View/Create/Edit | View | — | View | — | — |
| Weighment Gross/Tare | ✅ | ✅ | View | — | ✅ Create/Edit/Print | — | — |
| Purchase view/lock/unlock/cancel | ✅ | ✅ | View | View | View/Create/Edit | View (own only) | — |
| Payment / Pay / Cancel / Reverse (Phase 9) | ✅ | View/Export/Print | — | ✅ full | — | View (own) | — |
| Loan / Recovery (Phase 8) | ✅ | View/Export/Print | — | ✅ full incl. Cancel/Reverse | — | View (own) | — |
| Cash evidence capture | ✅ | View | — | ✅ View/Create | — | — | — |
| SalePurchase module (Phase 5+) | ✅ | — | — | — | — | — | ✅ View/Create/Edit/Print/Export |
| Reports / Export / Print | ✅ | ✅ | View/Export/Print | ✅ | View/Print | View (own) | View/Print |
| Camera live view | ✅ | ✅ | ✅ | — | ✅ | — (never factory-wide) | ✅ |
| Users management | ✅ | ✅ View/Create/Edit/Delete | — | — | — | — | — |
| Roles/Permissions edit | ✅ | View | — | — | — | — | — |
| Device/SMS/Backup/Print/Sound/WeightRule Configure | ✅ only | Backup.View/Configure | Backup.View | — | — | — | — |
| Audit log | ✅ | ✅ View | — | — | — | — | — |
| Health visibility | Full system | Business devices + app | Basic | App/API/DB | Digitizer/Camera/Printer/DB/LAN | Basic service status | SalePurchase devices |
| User Guide | Developer guide | Admin guide | SubAdmin guide | Accountant guide | Operator guide | Farmer guide | SalePurchase guide |

Notes:
- SalePurchase role explicitly has **no access** to developer configuration, user management,
  cane payment, loans, backup or security configuration.
- Farmer role additionally has its own `/api/farmer/dashboard` and `/api/farmer/statement`
  (Phase 12) which are scoped server-side to that user's own Grower record — no permission
  gate needed there since the endpoint itself never returns another farmer's data.
- Every action is enforced **server-side**; menu/button hiding in Flutter is cosmetic only.
- Custom roles can be created and granted any permission subset at runtime (Role.Create/Edit).
- Developer role permissions cannot be reduced; initial `developer` account cannot be deleted.
