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
        var guides = new List<object>();
        foreach (var role in roles.Where(Guides.ContainsKey))
            guides.Add(new { role, sections = Guides[role] });

        // Build this part from effective permissions rather than only the role
        // name. It therefore stays accurate for users with multiple/custom roles
        // and documents only forms that the logged-in user can actually open.
        var workflows = AvailableWorkflowSections();
        if (workflows.Count > 0)
            guides.Add(new { role = "My Available Menu Workflows", sections = workflows });
        return Ok(guides);
    }

    private record Section(string Title, string[] Steps);

    private static readonly string[] CommonFailures =
    {
        "Printer fails: check power/cable, verify printer name in Print Configuration, use Reprint after fixing. Weighment data is already saved - never re-weigh for a print problem.",
        "Device (digitizer) fails: check RS232/USB cable and COM port, ask Developer to run Test Communication. Do not save weighment without a valid live weight.",
        "Internet fails: local weighment (Gross/Tare/Print/Camera) continues on factory LAN. Only SMS/remote access pause and resume automatically."
    };

    private List<Section> AvailableWorkflowSections()
    {
        var sections = new List<Section>();
        void Add(string permission, string title, params string[] steps)
        {
            if (_current.HasPermission(permission))
                sections.Add(new Section(title, steps));
        }

        Add("Dashboard.View", "Dashboard",
            "Open Dashboard to see the modules and operational summary allowed for your account.",
            "The top bar shows company, season, current date/time, username and role. Use the account icon for Change Password, Logout or Logout All Sessions.",
            "Green indicates normal status, amber needs attention and red indicates a failure. Open the related menu for details or contact the administrator.");

        Add("Weighment.View", "Purchase Weighment — Gross Entry",
            "Select GROSS at the top of Purchase Weighment.",
            "Enter the six-digit Grower ID, for example 100001, and press ENTER or click Lookup. Verify the grower name, father name and village that appear.",
            "Select Vehicle Type and enter Vehicle Number. Use 4-15 letters/digits, for example MP20KR2012 or UP32AB1234; spaces and punctuation are normalized.",
            "Select Variety Type first. The matching Variety list then loads automatically; select the required variety.",
            "Other Cutting % and Cutting % load from Weight Rules. Keep the defaults or type an authorised value from 0.00 to 100.00 with up to two decimals.",
            "Confirm that the live digitizer weight is STABLE and is not below the configured minimum, then click SAVE GROSS.",
            "After a successful save, note the Purchase ID. If Auto Print is ON the configured printer prints automatically; otherwise the PDF opens for manual printing.",
            "Wait for the completion announcement and move the vehicle completely off the platform before starting the next vehicle.");

        Add("Weighment.Edit", "Purchase Weighment — Tare / Final Entry",
            "Select TARE. Choose the vehicle from Today's Gross (Pending Tare), or enter its Purchase ID and press ENTER.",
            "Verify grower, vehicle, gross weight, rate and gross time before continuing.",
            "Wait until the empty vehicle is on the platform and the live tare weight is STABLE. Tare must be lower than Gross.",
            "If the displayed rate is reduced, select Approved By and Rate Reason, enter the remark, and upload/capture the required JPG/JPEG evidence. A rate above the configured master rate is not allowed.",
            "Click SAVE TARE. The system calculates Net Weight, Cutting Weight, Other Deduction, Final Weight and Amount on the server.",
            "After success the row leaves the pending list. Auto Print prints configured copies; otherwise print the opened PDF. Never re-save a completed tare merely because printing failed.");

        Add("SalePurchase.View", "Sale Weighment — Tare and Gross",
            "Open Sale Weighment. This workflow normally records TARE first and GROSS after the vehicle is loaded.",
            "For TARE, select Item, Party and Vehicle Type; enter Vehicle Number, Driver Name and optional remark, then wait for a stable live weight and save.",
            "For GROSS, select the pending sale row or enter its Sale ID, verify the details, wait for a stable gross weight greater than tare and save.",
            "The active Sale Rate for the selected item is shown. If it is reduced, Approved By, Rate Reason, remark and JPG/JPEG evidence become mandatory; a value above master rate is blocked.",
            "If Auto Print is enabled the sale slip prints automatically. Otherwise its PDF opens for manual print. Move the vehicle off the platform after completion.");

        Add("Grower.View", "Grower Search and Grower Master",
            "Use Grower Search to find a farmer by six-digit Grower ID, name, father name, village or mobile.",
            "Grower Master lists identity, village, mobile and masked bank/Aadhaar details. Use the always-visible bottom scrollbar to view columns on the right.",
            _current.HasPermission("Grower.Create")
                ? "To create a grower, click New Grower, select Village, enter name/father/mobile and optional bank details, then Save. The system generates the next six-digit Grower ID automatically."
                : "Your access is read-only; create/edit controls are intentionally hidden.",
            _current.HasPermission("Grower.Edit")
                ? "Use Edit only for a genuine correction. Aadhaar is encrypted and account/Aadhaar values remain masked in lists."
                : "If a grower detail is incorrect, report it to an authorised Admin/Developer.");

        Add("Zone.View", "Masters",
            "Open Masters and choose a tab such as Zone, Village, Vehicle Type, Variety Type, Variety, Rates, Sale Rates, Rate Reasons, Items, Parties, Payment Modes, Loan Types or Expense List.",
            "Use Search to locate a record. Active records are used in transaction forms; Show inactive displays disabled records where supported.",
            "If New/Edit/Revise buttons are absent, the master is view-only for your account. The API also blocks unauthorised changes.",
            "Rate and Sale Rate history is versioned: an authorised user uses Revise/New Period rather than overwriting an old transaction rate.");

        Add("Purchase.View", "Purchases",
            "Use the search box to locate a purchase by Grower ID/name or other displayed details.",
            "Review Gross, Tare, Final Weight, Rate, Amount, lifecycle status and payment status. Drag the pinned bottom scrollbar to reach right-side columns.",
            "GROSS_DONE means tare is pending; TARE_DONE means final calculation is complete; CANCELLED records remain for audit history.");

        Add("Payment.View", "Payments",
            "Single Purchase is selected by default. Enter Grower/Purchase details and preview eligible completed purchases before paying.",
            "Verify gross/tare/final weight, total amount, loan deduction and net payable. Select payment mode and complete required cash/bank evidence.",
            "Click Save/Pay only after confirmation. Cancellation asks Yes/No and occurs only when Yes is selected; cancelled history is retained.",
            "Use the Payment Register and its bottom horizontal scrollbar to review status, evidence and actions.");

        Add("CashBook.View", "Cash Book",
            "Cash Receipt records money brought into the factory. Select source, enter source name, amount, date, reference and remark, then Save Cash Receipt.",
            "Other Cash Payment records non-farmer cash paid out. Farmer cash payments are posted automatically from Payments and must not be entered again.",
            "At the end of the working day open Day Closing, verify Opening + Cash In - Farmer Paid - Other Paid, enter Cash Taken By, then confirm. The complete server-calculated remaining cash is recorded as Day Closing Withdrawal; never enter the same withdrawal again under Other Cash Payment.",
            "After closing, that date is locked against new cash entries. On the next day record the physical cash brought back as a new Cash Receipt; the day then reconciles independently while the ledger history remains continuous.",
            "Future dates are disabled. Use Today or a From/To date range to review the Daily Cash Reconciliation table: Opening, Cash In, Farmer Paid, Other Paid, Total Spent, Before Close, Closing Withdrawal and Cash Left.",
            "Use the pinned bottom scrollbar to review transaction-level balance/reference columns; Print PDF or Export Excel creates the same day-wise reconciliation report.");

        Add("Expense.View", "Expenses",
            "Open Expenses, select the configured Expense Type, enter amount, date, payee/reference and remarks, then save if your account has Create permission.",
            "Use date/search filters to review entries. Edit/Delete controls appear only with the corresponding permission; financial history remains audited.");

        Add("Loan.View", "Loans",
            "Search the grower, select Loan Type, enter amount/date/details and save only after verifying the farmer and terms.",
            "Use the register to review principal, recovery and outstanding balance. Cancellation/reversal requires an authorised reason and keeps history.");

        Add("Report.View", "Reports",
            "Select a report such as Cane Purchase Report, Rate Change Approval, Payment, Sale Product, Loan, Daily Collection, Cash Book or Profit/Loss.",
            "Choose From Date and To Date, enter the optional Grower/Transaction/Vehicle/Status filter, then click Search.",
            "Use the pinned bottom scrollbar to inspect every column regardless of vertical position. Totals appear above the grid.",
            "Print (PDF) creates the formatted report; Export (Excel) downloads the same filtered data. Rate Change Approval includes approver, reason, remark and secure attachment View.");

        Add("Image.View", "View Images",
            "Select the required image category (Purchase Weighment, Payment, Sale Weighment or Rate Update), enter Purchase/Payment/Sale ID and search.",
            "Open an image to verify its transaction stage and timestamp. Missing images should be reported; never upload unrelated files.");

        if (_current.HasPermission("Weighment.Print") || _current.HasPermission("SalePurchase.Print") || _current.HasPermission("Payment.Print"))
            sections.Add(new Section("Print / Reprint", new[]
            {
                "Choose document type, enter the transaction ID, preview the saved document and print the required copies.",
                "Use Reprint when a saved transaction printed incorrectly. Do not repeat Gross/Tare/Payment just to obtain another slip.",
                "For dot-matrix printing, align continuous stationery at calibrated TOF and follow the tear/reset prompt after the dotted line."
            }));

        Add("User.View", "Users & Roles",
            "Click New User, enter username/full name/mobile and select only the required business role. Developer role/users are hidden from non-developer management.",
            "Give the temporary password securely. The user must change it on first login.",
            "Use Edit to activate/deactivate or correct roles. Deactivation revokes sessions; never share accounts between operators.");

        Add("Device.Configure", "Weighing Device",
            "Select the correct COM port and communication settings, then Test Communication before Activate/Save.",
            "Verify raw frames, parsed kilograms and STABLE status. A USB-to-serial adapter must appear correctly in Windows Device Manager.",
            "Only one program may open a COM port. Stop other scale utilities before testing the API device connection.");

        Add("WeightRule.Configure", "Configuration",
            "Weight Rules controls minimum weight, default Cutting %, default Other Deduction % and where each rule applies.",
            "Use Developer Unlock only after physically confirming the platform is empty; provide a clear audit reason.",
            "Print Configuration controls printer type, copies, Auto Print and dot-matrix calibration. Camera/SMS/Company/Backup settings affect production infrastructure and must be tested after changes.");

        Add("Audit.View", "Audit Log",
            "Filter Audit Log by user/action/date to investigate important create, update, cancel, unlock and configuration events.",
            "Audit history is read-only. Use the recorded old/new values and timestamp when investigating an incident.");

        return sections;
    }

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
                "If a restart leaves the vehicle-clear safety lock stuck, physically verify that the platform is empty, then use Configuration → Weight Rules → Developer Unlock and enter an audit reason.",
                "Sound Configuration: language (Hindi/English), volume, speech rate, repeat mode (OFF/ONCE/TWICE/CONTINUOUS) and interval.",
                "Edit announcement text per event: Below Minimum, Weighing Active, Weighment Completed." }),
            new Section("5. Camera / SMS / Reports / Farmer Portal / Backup", new[]{
                "Cameras 1-6: vendor (Hikvision/CP Plus/Dahua/Uniview/ONVIF/RTSP), IP, port, credentials (stored encrypted), capture & live-view switches.",
                "Global switches: CameraSystemEnabled and ImageCaptureEnabled control all transaction captures.",
                "SMS: generic HTTP provider - API URL, key/secret (encrypted), sender ID, DLT templates. Test before enabling.",
                "Reports: Purchase/Payment/Loan/Daily Collection and Rate Change Approval - filter records, view totals, Print (PDF) or Export (Excel). The Rate Change Approval report shows Cane/Sale master rate, approved rate, approver, reason, remark and secure attachment preview.",
                "Farmer Portal: farmers get a read-only My Dashboard + Statement, always scoped to their own account only.",
                "Print: printer type (Dot Matrix/A4), copies per document, Auto Print switch. Dot Matrix cane/sale slips use two fixed half-page sections; turn Print Header OFF for pre-printed stationery and tune Header Reserved Height/Page Lines for the installed paper.",
                "Backup: Frequency/Time/Retention/Folder policy - generates the SQL backup script and Task Scheduler XML for you.",
                "Storage root default D:\\CanePaymentData - configurable in System Settings. Backup guide is in docs/BACKUP_GUIDE.md." }),
            new Section("6. Security & Users", new[]{
                "Create Admin first, then other users. No public registration exists.",
                "The system Developer identity and Developer role are intentionally hidden from User Management and cannot be assigned there.",
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
                "Maintain Zone → Village → Grower hierarchy. Village IDs start at 101; public Grower IDs are six digits starting at 100001.",
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
                "Your screen is the Unified Purchase Weighment form - GROSS and TARE radio buttons on one form.",
                "Live weight, stability, device status and enabled cameras are always visible." }),
            new Section("2. GROSS Step-by-step", new[]{
                "1. Select (O) GROSS.",
                "2. Type the 6-digit Grower ID (Example: 100001) and press ENTER - name/father/village appear read-only.",
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
                "Cutting % / Other Deduction % accept 0-100 with 2 decimals (Example: 2.00).",
                "Duplicate save/double-click is blocked by the server." }),
            new Section("5. Common Failures", CommonFailures)
        },
        ["Farmer"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You can see only YOUR OWN data: purchases, weights, rate, amounts, payment and loan status.",
                "You cannot edit any official transaction." }),
            new Section("2. First Login", new[]{
                "Login with your registered 10-digit mobile number.",
                "Initial password is the first 4 characters of your name in UPPERCASE followed by your registered mobile number.",
                "You must create a new private password before the dashboard opens. The initial password stops working immediately." }),
            new Section("3. Using the App", new[]{
                "Dashboard shows your recent weighments with Gross/Tare/Final weight in Quintal (2 decimals).",
                "Expand All Purchases, All Payments or All Loans to see your complete read-only history.",
                "Payment status: PENDING means tare is done and payment is queued; PAID shows the advice number.",
                "If a value looks wrong, contact the factory office - records are corrected only through the official audited process." }),
            new Section("4. When something fails", new[]{
                "Farmer login outside the server computer requires an HTTPS API address.",
                "No internet: the app cannot load remote data; factory operations continue and your data appears when you are back online." })
        },
        ["SalePurchase"] = new object[]
        {
            new Section("1. Overview", new[]{
                "You operate the separate Sugar/Gud/Bagasse/Molasses SalePurchase weighment module.",
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
