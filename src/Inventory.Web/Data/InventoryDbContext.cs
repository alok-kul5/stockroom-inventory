using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Data;

public class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<StockLevel> StockLevels => Set<StockLevel>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Product>().HasIndex(p => p.Sku).IsUnique();
        b.Entity<Location>().HasIndex(l => l.Code).IsUnique();

        b.Entity<StockLevel>().HasKey(s => new { s.ProductId, s.LocationId });
        b.Entity<StockLevel>().ToTable(t => t.HasCheckConstraint("CK_StockLevel_NonNegative", "[Quantity] >= 0"));

        b.Entity<StockMovement>().ToTable(t => t.HasCheckConstraint("CK_StockMovement_Positive", "[Quantity] > 0"));
        b.Entity<StockMovement>().HasIndex(m => m.CreatedAtUtc);
        b.Entity<StockMovement>().Property(m => m.Type).HasConversion<string>().HasMaxLength(16);

        // A product or location with history can't be deleted out from under its ledger.
        b.Entity<StockMovement>().HasOne(m => m.Product).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>().HasOne(m => m.FromLocation).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockMovement>().HasOne(m => m.ToLocation).WithMany().OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockLevel>().HasOne(s => s.Product).WithMany(p => p.StockLevels).OnDelete(DeleteBehavior.Restrict);
        b.Entity<StockLevel>().HasOne(s => s.Location).WithMany(l => l.StockLevels).OnDelete(DeleteBehavior.Restrict);
    }
}
