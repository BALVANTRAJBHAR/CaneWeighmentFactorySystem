namespace CaneFactory.Application.DTOs;

public class LoanIssueRequest
{
    public string GrowerCode { get; set; } = string.Empty;
    public int LoanTypeId { get; set; }
    public decimal LoanAmount { get; set; }
    public string? Remarks { get; set; }
    public string? IdempotencyKey { get; set; }
}

public class LoanRecoveryRequest
{
    public int LoanId { get; set; }
    public decimal RecoveryAmount { get; set; }
    public string? Remarks { get; set; }
    public string? IdempotencyKey { get; set; }
}
