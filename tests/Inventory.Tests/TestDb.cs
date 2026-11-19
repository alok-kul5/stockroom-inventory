using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests;

/// <summary>
/// A real relational database (SQLite in memory) so constraints, unique indexes and
/// concurrency tokens behave like they do on SQL Server, unlike the EF in-memory provider.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDb()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        using var db = NewContext();
        db.Database.EnsureCreated();
    }

    public InventoryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>().UseSqlite(_connection).Options);

    /// <summary>Two locations and two products, no stock.</summary>
    public (int productA, int productB, int pune, int mumbai) SeedBasics()
    {
        using var db = NewContext();
        var pune = new Location { Code = "PNQ", Name = "Pune" };
        var mumbai = new Location { Code = "BOM", Name = "Mumbai" };
        var a = new Product { Sku = "A-1", Name = "Product A", UnitPrice = 100, ReorderLevel = 5 };
        var b = new Product { Sku = "B-1", Name = "Product B", UnitPrice = 50, ReorderLevel = 10 };
        db.AddRange(pune, mumbai, a, b);
        db.SaveChanges();
        return (a.Id, b.Id, pune.Id, mumbai.Id);
    }

    public void Dispose() => _connection.Dispose();
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
