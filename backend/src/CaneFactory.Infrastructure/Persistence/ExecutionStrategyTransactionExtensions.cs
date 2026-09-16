using Microsoft.EntityFrameworkCore;

namespace CaneFactory.Infrastructure.Persistence;

/// <summary>Runs the entire user transaction inside EF Core's configured retry strategy.
/// Sequence reservations and dependent writes therefore commit or roll back together.</summary>
public static class ExecutionStrategyTransactionExtensions
{
    public static Task ExecuteInTransactionAsync(this DbContext db, Func<Task> action,
        System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.ReadCommitted)
        => db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(isolationLevel);
            await action();
            await transaction.CommitAsync();
        });
}
