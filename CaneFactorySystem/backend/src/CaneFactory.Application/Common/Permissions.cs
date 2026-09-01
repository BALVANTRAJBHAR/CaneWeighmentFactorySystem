namespace CaneFactory.Application.Common;

public static class Actions
{
    public const string View = "View";
    public const string Create = "Create";
    public const string Edit = "Edit";
    public const string Delete = "Delete";
    public const string Approve = "Approve";
    public const string Lock = "Lock";
    public const string Unlock = "Unlock";
    public const string Cancel = "Cancel";
    public const string Reverse = "Reverse";
    public const string Pay = "Pay";
    public const string Export = "Export";
    public const string Print = "Print";
    public const string Configure = "Configure";
    public const string ViewCamera = "ViewCamera";
    public const string ViewSensitiveData = "ViewSensitiveData";
}

public static class Modules
{
    public static readonly string[] Masters =
    {
        "Zone", "Village", "Bank", "Grower", "Vehicle", "VarietyType", "Variety",
        "Rate", "Item", "Party", "Season", "PaymentMode", "Company"
    };

    public static readonly string[] All =
    {
        "Zone", "Village", "Bank", "Grower", "Vehicle", "VarietyType", "Variety",
        "Rate", "Item", "Party", "Season", "PaymentMode", "Company",
        "User", "Role", "Permission",
        "Purchase", "Weighment", "SalePurchase",
        "Loan", "LoanRecovery", "Payment", "CashEvidence",
        "Device", "Camera", "Sms", "Razorpay", "Print", "Sound", "WeightRule",
        "Audit", "Report", "Dashboard", "Image", "Backup", "SystemSetting", "UserGuide", "Health"
    };
}

public static class Perm
{
    public static string Of(string module, string action) => $"{module}.{action}";

    // frequently used codes
    public const string WeighmentGross = "Weighment.Create";
    public const string WeighmentTare = "Weighment.Edit";
    public const string DeviceConfigure = "Device.Configure";
}
