namespace CaneFactory.Application.DTOs;

/// <summary>Phase 11: flat row projections used by ReportsController for on-screen JSON and PDF/Excel export.</summary>
public class PurchaseReportRow
{
    public int PurchaseId { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public string GrowerName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string VarietyName { get; set; } = string.Empty;
    public decimal GrossWeightQuintal { get; set; }
    public DateTime GrossDateTime { get; set; }
    public decimal? TareWeightQuintal { get; set; }
    public decimal? NetWeightQuintal { get; set; }
    public decimal? FinalWeightQuintal { get; set; }
    public decimal Rate { get; set; }
    public decimal? PurchaseAmount { get; set; }
    public string GrossTareStatus { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
    public string LockStatus { get; set; } = string.Empty;
}

public class PaymentReportRow
{
    public int PaymentId { get; set; }
    public int AdviceNumber { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public string GrowerName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;
    public decimal TotalPurchaseAmount { get; set; }
    public decimal LoanDeductedAmount { get; set; }
    public decimal NetPayableAmount { get; set; }
    public string PaymentModeName { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string PaidByUserName { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;
}

public class LoanReportRow
{
    public int LoanId { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public string GrowerName { get; set; } = string.Empty;
    public string VillageName { get; set; } = string.Empty;
    public string LoanTypeName { get; set; } = string.Empty;
    public decimal LoanAmount { get; set; }
    public decimal RecoveredAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public DateTime IssueDate { get; set; }
    public string IssuedByUserName { get; set; } = string.Empty;
    public string LoanStatus { get; set; } = string.Empty;
}

public class DailyCollectionRow
{
    public DateTime Date { get; set; }
    public int VehicleCount { get; set; }
    public decimal FinalWeightQuintal { get; set; }
    public decimal PurchaseAmount { get; set; }
}
