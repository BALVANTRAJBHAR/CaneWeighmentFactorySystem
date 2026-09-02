using CaneFactory.API.Auth;
using CaneFactory.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace CaneFactory.API.Controllers;

/// <summary>Role-specific User Guide. Only guides matching the logged-in user's roles are returned.</summary>
[ApiController]
[Route("api/user-guide")]
public class UserGuideController : ControllerBase
{
    private readonly ICurrentUser _current;
    public UserGuideController(ICurrentUser current) => _current = current;

    [HasPermission("UserGuide.View")]
    [HttpGet]
    public IActionResult Get()
    {
        var roles = (_current.Role ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var guides = roles.Where(Guides.ContainsKey).Select(r => new { role = r, sections = Guides[r] }).ToList();
        return Ok(guides);
    }

    private record Section(string Title, string[] Steps);

    private static readonly string[] CommonFailures =
    {
        "Printer fails: check power/cable, verify printer name in Print Configuration, use Reprint after fixing. Weighment data is already saved - never re-weigh for a print problem.",
        "Device (digitizer) fails: check RS232/USB cable and COM port, ask Developer to run Test Communication. Do not save weighment without a valid live weight.",
        "Internet fails: local weighment (Gross/Tare/Print/Camera) continues on factory LAN. Only SMS/remote access pause and resume automatically."
    };

    private static readonly Dictionary<string, object> Guides = new()
    {
        ["Developer"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You have full system access: configuration, security, devices, users and all business modules.",
                "First login uses the temporary password - the system forces a password change immediately." }),
            new Section("2. Dashboard", new[]{
                "Header shows Company, Date/Time, your name/role, and full system health (CPU/RAM/Storage/DB/Digitizer/Cameras/Printer/SMS/Backup).",
                "Green=Normal, Yellow=Warning, Red=Critical. Thresholds are configurable under System Settings." }),
            new Section("3. Device Configuration (Digitizer)", new[]{
                "Developer Dashboard → Weighing Device: set COM Port, Baud Rate (2400 for supplied indicator), Parity=None, Data Bits=8, Stop Bits=1, Flow Control=None.",
                "String Profiles: 'String Type 15' preset is pre-seeded (STX 0x02, sign 0x20/0x2D, 6 weight chars, ETX 0x03, CR LF).",
                "Use Test Communication: Connect → Start Reading → verify RAW HEX and parsed weight → Save → Activate.",
                "Use Test Parser with sample hex: 02 20 30 30 31 35 30 30 03 0D 0A → expect +1500 KG, FRAME VALID.",
                "Only one device configuration can be ACTIVE. History of every change is stored and auditable." }),
            new Section("4. Weight Rules & Sound", new[]{
                "Configure Minimum Weight (Quintal), apply-to flags for Gross/Tare/CanePurchase/SalePurchase.",
                "Sound Configuration: language (Hindi/English), volume, speech rate, repeat mode (OFF/ONCE/TWICE/CONTINUOUS) and interval.",
                "Edit announcement text per event: Below Minimum, Weighing Active, Weighment Completed." }),
            new Section("5. Camera / SMS / Reports / Farmer Portal / Backup", new[]{
                "Cameras 1-6: vendor (Hikvision/CP Plus/Dahua/Uniview/ONVIF/RTSP), IP, port, credentials (stored encrypted), capture & live-view switches.",
                "Global switches: CameraSystemEnabled and ImageCaptureEnabled control all transaction captures.",
                "SMS: generic HTTP provider - API URL, key/secret (encrypted), sender ID, DLT templates. Test before enabling.",
                "Reports: Purchase/Payment/Loan/Daily Collection - filter by date/village/grower, view totals, Print (PDF) or Export (Excel).",
                "Farmer Portal: farmers get a read-only My Dashboard + Statement, always scoped to their own account only.",
                "Print: printer type (Dot Matrix/A4), copies per document, Auto Print switch.",
                "Backup: Frequency/Time/Retention/Folder policy - generates the SQL backup script and Task Scheduler XML for you.",
                "Storage root default D:\\CanePaymentData - configurable in System Settings. Backup guide is in docs/BACKUP_GUIDE.md." }),
            new Section("6. Security & Users", new[]{
                "Create Admin first, then other users. No public registration exists.",
                "Every new user gets a temporary password and must change it at first login.",
                "Deactivating a user instantly revokes refresh tokens; access tokens die within 60 seconds.",
                "Audit Log screen shows every critical action with old/new values." }),
            new Section("7. Common Failures", CommonFailures)
        },
        ["Admin"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You manage business operations: masters, growers, purchases, locking, reports and users.",
                "Developer-only infrastructure (device/SMS/backup credentials) is not visible to you by design." }),
            new Section("2. Masters", new[]{
                "Maintain Zone → Village → Grower hierarchy. Village IDs start at 101; grower codes are VillageId/Sequence (e.g. 101/1).",
                "All masters validate duplicates server-side; soft delete only - business-critical records with transactions cannot be deleted.",
                "Rate Master is versioned: create a new rate period instead of editing old rates. Unpaid recalculation requires Approve permission with preview + confirmation." }),
            new Section("3. Purchases & Locking", new[]{
                "Purchases list shows full weighment lifecycle. Lock prevents any further change including payment.",
                "Cancellation requires a reason and is fully audited. Paid purchases cannot be cancelled - reverse the payment first (Accountant)." }),
            new Section("4. Reports & Export", new[]{
                "Every grid supports search, sort, active/inactive filter, and Excel/PDF/Print using the company report layout." }),
            new Section("5. Common Failures", CommonFailures)
        },
        ["SubAdmin"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You can view and maintain masters and view purchases/reports as permitted.",
                "Any action button you do not see means you lack that permission - the server enforces this too." }),
            new Section("2. Daily Work", new[]{
                "Use Grower Search (Name/Father Name/Village radio buttons) to locate farmers quickly.",
                "Keep Village/Grower/Vehicle masters clean; duplicates are blocked server-side." }),
            new Section("3. Common Failures", CommonFailures)
        },
        ["Accountant"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You handle payments, loans, recoveries, cancellations/reversals and financial reports.",
                "Payment / Loan modules activate in Phase 8-9; screens appear automatically when enabled." }),
            new Section("2. Payment Workflow (when enabled)", new[]{
                "Validate: tare completed, final weight valid, not locked, not already paid, loan checked.",
                "Advice numbers start from 1 and are generated transaction-safe - two operators can never get the same advice.",
                "Reversal creates reversal records; history is never deleted. A reversed purchase becomes payable again with a NEW advice number." }),
            new Section("3. Lock/Cancel/Reversal Rules", new[]{
                "Every cancel/reverse requires a reason and authorization, and is written to the audit log with old/new values." }),
            new Section("4. Common Failures", CommonFailures)
        },
        ["Operator"] = new object[]
        {
            new Section("1. Overview", new[]{
                "Your screen is the Unified Cane Weighment form - GROSS and TARE radio buttons on one form.",
                "Live weight, stability, device status and enabled cameras are always visible." }),
            new Section("2. GROSS Step-by-step", new[]{
                "1. Select (O) GROSS.",
                "2. Type Grower Code (Example: 101/1) and press ENTER - name/father/village appear read-only.",
                "3. Select Vehicle Type, enter Vehicle Number (Example: UP32AB1234).",
                "4. Select Variety Type - the Variety list loads automatically for that type.",
                "5. Wait for a stable live weight at or above the minimum weight.",
                "6. Press Save Gross (or Enter). Success popup shows Purchase ID and Gross Weight.",
                "7. Slip auto-prints when Auto Print is ON; sound announces completion." }),
            new Section("3. TARE Step-by-step", new[]{
                "1. Select (O) TARE - the same form switches to Tare mode.",
                "2. Double-click a row in the Pending grid OR type Purchase ID and press ENTER.",
                "3. Verify the read-only purchase details (grower, vehicle, gross weight, rate).",
                "4. Wait for stable live tare weight (must be LESS than gross).",
                "5. Press Save Tare. Success popup shows Final Weight; purchase becomes Payment Pending.",
                "6. The row disappears from the pending grid automatically." }),
            new Section("4. Validation Rules", new[]{
                "Below-minimum weight blocks saving and plays the configured announcement.",
                "Cutting % / Tax % accept 0-100 with 2 decimals (Example: 2.00).",
                "Duplicate save/double-click is blocked by the server." }),
            new Section("5. Common Failures", CommonFailures)
        },
        ["Farmer"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You can see only YOUR OWN data: purchases, weights, rate, amounts, payment and loan status.",
                "You cannot edit any official transaction." }),
            new Section("2. Using the App", new[]{
                "Dashboard shows your recent weighments with Gross/Tare/Final weight in Quintal (2 decimals).",
                "Payment status: PENDING means tare is done and payment is queued; PAID shows the advice number.",
                "If a value looks wrong, contact the factory office - records are corrected only through the official audited process." }),
            new Section("3. When something fails", new[]{
                "No internet: the app cannot load remote data; factory operations continue and your data appears when you are back online." })
        },
        ["SalePurchase"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You operate the Sugar/Gud/Bagasse/Molasses SalePurchase weighment (module activates fully in a later phase).",
                "You do NOT have access to cane payment, loans, user management or developer configuration." }),
            new Section("2. Workflow (TARE first)", new[]{
                "1. Select Item and Party, Vehicle Type/Number, Driver Name, Remark.",
                "2. Save TARE - the vehicle appears in the pending tare grid.",
                "3. After loading, select the pending row and save GROSS.",
                "4. Rate/Amount are OPTIONAL - leave rate empty and amount stays empty.",
                "5. Print slips (two copies when configured); SMS goes to configured recipients." }),
            new Section("3. Common Failures", CommonFailures)
        }
    };
}
