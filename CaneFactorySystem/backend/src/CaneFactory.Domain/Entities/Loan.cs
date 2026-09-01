using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

public class LoanTypeMaster : BaseEntity
{
    public string LoanTypeName { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class Loan : BaseEntity
{
    public int GrowerId { get; set; }
    public Grower Grower { get; set; } = null!;
    public string GrowerCode { get; set; } = string.Empty;
    public int VillageId { get; set; }
    public int LoanTypeId { get; set; }
    public LoanTypeMaster LoanType { get; set; } = null!;
    public decimal LoanAmount { get; set; }
    public decimal RecoveredAmount { get; set; }
    public decimal OutstandingAmount { get; set; }
    public DateTime IssueDate { get; set; }
    public int IssuedByUserId { get; set; }
    public string IssuedByUserName { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string LoanStatus { get; set; } = "ACTIVE"; // ACTIVE | CLOSED | CANCELLED
    public int SeasonId { get; set; }
    public Season Season { get; set; } = null!;

    // Phase 7 print/reprint audit counter (0 = never printed; >0 subsequent prints are audited as Reprint)
    public int PrintCount { get; set; }
}

public class LoanRecovery : BaseEntity
{
    public int LoanId { get; set; }
    public Loan Loan { get; set; } = null!;
    public int GrowerId { get; set; }
    public string GrowerCode { get; set; } = string.Empty;
    public decimal RecoveryAmount { get; set; }
    public DateTime RecoveryDate { get; set; }
    public int RecoveredByUserId { get; set; }
    public string RecoveredByUserName { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public string RecoveryStatus { get; set; } = "ACTIVE"; // ACTIVE | REVERSED
    public int PrintCount { get; set; }
}
