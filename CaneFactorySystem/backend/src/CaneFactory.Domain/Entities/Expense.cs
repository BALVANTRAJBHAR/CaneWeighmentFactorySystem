using CaneFactory.Domain.Common;

namespace CaneFactory.Domain.Entities;

/// <summary>Configurable expense names used by the Expense entry screen.</summary>
public class ExpenseType : BaseEntity
{
    public string ExpenseName { get; set; } = string.Empty;
    public string? ExpenseNameHi { get; set; }
    public string? Description { get; set; }
}

/// <summary>A recorded business expense. TotalAmount is always recalculated by the API.</summary>
public class Expense : BaseEntity
{
    public int ExpenseTypeId { get; set; }
    public ExpenseType ExpenseType { get; set; } = null!;
    public DateTime ExpenseDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCharge { get; set; }
    public decimal TotalAmount { get; set; }
    public string? Remarks { get; set; }
}
