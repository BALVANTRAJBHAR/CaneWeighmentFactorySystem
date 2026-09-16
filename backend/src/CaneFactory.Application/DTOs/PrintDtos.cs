namespace CaneFactory.Application.DTOs;

/// <summary>
/// Centralized, module-agnostic print layout model (Phase 7). Any transaction (Gross/Tare now;
/// Payment/Loan/LoanRecovery/SalePurchase/Reports later) builds ONE of these and both renderers
/// (A4 PDF / DotMatrix ESC-P) draw the identical logo+company+QR+two-column standard from it -
/// no per-module print engine is ever duplicated.
/// </summary>
public class PrintDocument
{
    public string Language { get; set; } = "hi"; // "hi" | "en" - which Title/Label set is shown
    public string TitleHindi { get; set; } = string.Empty;
    public string TitleEnglish { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? LogoPath { get; set; }
    public string? SeasonName { get; set; }

    /// <summary>Safe identifier only for the QR (PurchaseId/AdviceNumber/LoanId) - never secrets/Aadhaar.</summary>
    public int? QrValue { get; set; }
    public string GeneratedByUserName { get; set; } = string.Empty;
    public DateTime PrintDateTime { get; set; } = DateTime.Now;

    /// <summary>Rendered two-per-line (left pair / right pair) by both renderers.</summary>
    public List<PrintRow> Rows { get; set; } = new();

    /// <summary>Evidence images rendered below the weighment details on A4 slips.</summary>
    public List<PrintImage> Images { get; set; } = new();
    public bool PrintImages { get; set; } = true;
}

public class PrintRow
{
    public string LabelHindi { get; set; } = string.Empty;
    public string LabelEnglish { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public PrintRow() { }
    public PrintRow(string labelHindi, string labelEnglish, string value)
    {
        LabelHindi = labelHindi; LabelEnglish = labelEnglish; Value = value;
    }
}

public class PrintImage
{
    public string Label { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;

    public PrintImage() { }
    public PrintImage(string label, string filePath)
    {
        Label = label;
        FilePath = filePath;
    }
}
