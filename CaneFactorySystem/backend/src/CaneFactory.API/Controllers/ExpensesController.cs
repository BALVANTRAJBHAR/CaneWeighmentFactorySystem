using CaneFactory.Application.Common;
using CaneFactory.Application.Interfaces;
using CaneFactory.Domain.Entities;
using CaneFactory.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaneFactory.API.Controllers;

[Route("api/expense-types")]
public class ExpenseTypesController : MasterControllerBase<ExpenseType>
{
    public ExpenseTypesController(AppDbContext db, IAuditService audit, ICurrentUser current, ISequenceGenerator seq)
        : base(db, audit, current, seq) { }

    protected override string Module => "ExpenseType";
    protected override string DisplayName(ExpenseType e) => e.ExpenseName;
    protected override System.Linq.Expressions.Expression<Func<ExpenseType, object>> DisplayNameExpr() => e => e.ExpenseName;
    protected override IQueryable<ExpenseType> ApplySearch(IQueryable<ExpenseType> q, string s) =>
        q.Where(x => x.ExpenseName.Contains(s) || (x.ExpenseNameHi != null && x.ExpenseNameHi.Contains(s)));

    protected override async Task<string?> ValidateAsync(ExpenseType e, int? existingId)
    {
        e.ExpenseName = Validators.Norm(e.ExpenseName);
        e.ExpenseNameHi = string.IsNullOrWhiteSpace(e.ExpenseNameHi) ? null : e.ExpenseNameHi.Trim();
        e.Description = string.IsNullOrWhiteSpace(e.Description) ? null : e.Description.Trim();
        if (e.ExpenseName.Length == 0) return "Expense name is required.";
        if (e.ExpenseName.Length > 100) return "Expense name must be at most 100 characters.";
        if (await Db.ExpenseTypes.AnyAsync(x => !x.IsDeleted && x.Id != existingId &&
            x.ExpenseName.ToLower() == e.ExpenseName.ToLower()))
            return $"Expense '{e.ExpenseName}' already exists.";
        return null;
    }

    protected override void ApplyUpdate(ExpenseType target, ExpenseType source)
    {
        target.ExpenseName = source.ExpenseName;
        target.ExpenseNameHi = source.ExpenseNameHi;
        target.Description = source.Description;
    }

    protected override async Task<string?> InUseReasonAsync(int id) =>
        await Db.Expenses.AnyAsync(x => x.ExpenseTypeId == id && !x.IsDeleted)
            ? "This expense name is used by saved expenses and cannot be deleted."
            : null;
}

public sealed class ExpenseSaveRequest
{
    public int ExpenseTypeId { get; set; }
    public DateTime? ExpenseDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitCharge { get; set; }
    public string? Remarks { get; set; }
}

[Route("api/expenses")]
public class ExpensesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IAuditService _audit;

    public ExpensesController(AppDbContext db, ICurrentUser current, IAuditService audit)
    {
        _db = db; _current = current; _audit = audit;
    }

    private IActionResult? Deny(string action) =>
        _current.HasPermission($"Expense.{action}")
            ? null
            : StatusCode(403, new { message = $"You do not have 'Expense.{action}' permission." });

    private IQueryable<Expense> Base() => _db.Expenses.AsNoTracking()
        .Where(x => !x.IsDeleted).Include(x => x.ExpenseType);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] int? expenseTypeId, [FromQuery] string? search, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100)
    {
        if (Deny("View") is { } denied) return denied;
        if (page < 1) page = 1;
        if (pageSize is < 1 or > 500) pageSize = 100;
        var q = Base();
        if (fromDate.HasValue) q = q.Where(x => x.ExpenseDate >= fromDate.Value.Date);
        if (toDate.HasValue) q = q.Where(x => x.ExpenseDate < toDate.Value.Date.AddDays(1));
        if (expenseTypeId.HasValue) q = q.Where(x => x.ExpenseTypeId == expenseTypeId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.ExpenseType.ExpenseName.Contains(s) ||
                (x.Remarks != null && x.Remarks.Contains(s)));
        }
        var totalCount = await q.CountAsync();
        var totalAmount = await q.SumAsync(x => (decimal?)x.TotalAmount) ?? 0;
        var items = await q.OrderByDescending(x => x.ExpenseDate).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                id = x.Id, expenseTypeId = x.ExpenseTypeId, expenseName = x.ExpenseType.ExpenseName,
                expenseNameHi = x.ExpenseType.ExpenseNameHi, expenseDate = x.ExpenseDate,
                quantity = x.Quantity, unitCharge = x.UnitCharge, totalAmount = x.TotalAmount,
                remarks = x.Remarks, createdAt = x.CreatedAt
            }).ToListAsync();
        return Ok(new { items, totalCount, page, pageSize, totals = new { totalCount, totalAmount } });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        if (Deny("View") is { } denied) return denied;
        var item = await Base().Where(x => x.Id == id).Select(x => new
        {
            id = x.Id, expenseTypeId = x.ExpenseTypeId, expenseName = x.ExpenseType.ExpenseName,
            expenseDate = x.ExpenseDate, quantity = x.Quantity, unitCharge = x.UnitCharge,
            totalAmount = x.TotalAmount, remarks = x.Remarks
        }).FirstOrDefaultAsync();
        return item == null ? NotFound(new { message = "Expense not found." }) : Ok(item);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ExpenseSaveRequest request)
    {
        if (Deny("Create") is { } denied) return denied;
        var error = await ValidateAsync(request);
        if (error != null) return BadRequest(new { message = error });
        var item = new Expense
        {
            ExpenseTypeId = request.ExpenseTypeId,
            ExpenseDate = (request.ExpenseDate ?? DateTime.Now).Date,
            Quantity = decimal.Round(request.Quantity, 3),
            UnitCharge = decimal.Round(request.UnitCharge, 2),
            TotalAmount = decimal.Round(request.Quantity * request.UnitCharge, 2),
            Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim(),
            CreatedAt = DateTime.UtcNow, CreatedBy = _current.UserId, Status = true
        };
        _db.Expenses.Add(item);
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Create", "Expense", nameof(Expense), item.Id.ToString(), newValue: item);
        return Ok(new { message = "Expense saved successfully.", id = item.Id, totalAmount = item.TotalAmount });
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] ExpenseSaveRequest request)
    {
        if (Deny("Edit") is { } denied) return denied;
        var item = await _db.Expenses.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (item == null) return NotFound(new { message = "Expense not found." });
        var error = await ValidateAsync(request);
        if (error != null) return BadRequest(new { message = error });
        item.ExpenseTypeId = request.ExpenseTypeId;
        item.ExpenseDate = (request.ExpenseDate ?? DateTime.Now).Date;
        item.Quantity = decimal.Round(request.Quantity, 3);
        item.UnitCharge = decimal.Round(request.UnitCharge, 2);
        item.TotalAmount = decimal.Round(request.Quantity * request.UnitCharge, 2);
        item.Remarks = string.IsNullOrWhiteSpace(request.Remarks) ? null : request.Remarks.Trim();
        item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Edit", "Expense", nameof(Expense), id.ToString(), newValue: item);
        return Ok(new { message = "Expense updated successfully.", totalAmount = item.TotalAmount });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        if (Deny("Delete") is { } denied) return denied;
        var item = await _db.Expenses.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted);
        if (item == null) return NotFound(new { message = "Expense not found." });
        item.IsDeleted = true; item.Status = false; item.DeletedAt = DateTime.UtcNow; item.DeletedBy = _current.UserId;
        await _db.SaveChangesAsync();
        await _audit.LogAsync("Delete", "Expense", nameof(Expense), id.ToString());
        return Ok(new { message = "Expense deleted successfully." });
    }

    private async Task<string?> ValidateAsync(ExpenseSaveRequest request)
    {
        if (request.ExpenseTypeId <= 0) return "Expense name is required.";
        if (!await _db.ExpenseTypes.AnyAsync(x => x.Id == request.ExpenseTypeId && x.Status && !x.IsDeleted))
            return "Selected expense name is invalid or inactive.";
        if (request.Quantity <= 0) return "Quantity must be greater than zero.";
        if (request.UnitCharge < 0) return "Per-unit charge cannot be negative.";
        return null;
    }
}
