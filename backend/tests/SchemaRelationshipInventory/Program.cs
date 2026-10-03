using CaneFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

// Model-only inventory: this never connects to or changes a database.
var options = new DbContextOptionsBuilder<AppDbContext>()
    .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=SchemaInventory;Trusted_Connection=True")
    .Options;
using var db = new AppDbContext(options);

var rows = db.Model.GetEntityTypes()
    .SelectMany(dependent => dependent.GetForeignKeys().Select(fk =>
    {
        var dependentTable = dependent.GetTableName() ?? dependent.ClrType.Name;
        var principalTable = fk.PrincipalEntityType.GetTableName() ?? fk.PrincipalEntityType.ClrType.Name;
        var store = StoreObjectIdentifier.Table(dependentTable, dependent.GetSchema());
        var columns = string.Join(",", fk.Properties.Select(p => p.GetColumnName(store) ?? p.Name));
        return new
        {
            Principal = principalTable,
            Dependent = dependentTable,
            Columns = columns,
            Delete = fk.DeleteBehavior.ToString(),
            Required = fk.IsRequired
        };
    }))
    .OrderBy(x => x.Principal)
    .ThenBy(x => x.Dependent)
    .ThenBy(x => x.Columns)
    .ToList();

Console.WriteLine("PrincipalTable\tDependentTable\tForeignKeyColumns\tDeleteBehavior\tRequired");
foreach (var row in rows)
    Console.WriteLine($"{row.Principal}\t{row.Dependent}\t{row.Columns}\t{row.Delete}\t{row.Required}");
Console.WriteLine($"RELATIONSHIPS={rows.Count}");
