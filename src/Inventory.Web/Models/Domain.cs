using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Web.Models;

public class Product
{
    public int Id { get; set; }

    [Required, StringLength(32)]
    [RegularExpression(@"^[A-Z0-9\-]+$", ErrorMessage = "Use capital letters, digits and dashes only.")]
    public string Sku { get; set; } = "";

    [Required, StringLength(120)]
    public string Name { get; set; } = "";

    [StringLength(500)]
    public string? Description { get; set; }

    [Range(0, 10_000_000)]
    [Column(TypeName = "decimal(18,2)")]
    [Display(Name = "Unit price (₹)")]
    public decimal UnitPrice { get; set; }

    /// <summary>Total stock across all locations at or below this level counts as low stock.</summary>
    [Range(0, 1_000_000)]
    [Display(Name = "Reorder level")]
    public int ReorderLevel { get; set; }

    public bool IsArchived { get; set; }

    public List<StockLevel> StockLevels { get; set; } = new();
}

public class Location
{
    public int Id { get; set; }

    [Required, StringLength(16)]
    [RegularExpression(@"^[A-Z0-9\-]+$", ErrorMessage = "Use capital letters, digits and dashes only.")]
    public string Code { get; set; } = "";

    [Required, StringLength(80)]
    public string Name { get; set; } = "";

    public List<StockLevel> StockLevels { get; set; } = new();
}

/// <summary>On-hand quantity of one product at one location. Composite key (ProductId, LocationId).</summary>
public class StockLevel
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int LocationId { get; set; }
    public Location Location { get; set; } = null!;

    public int Quantity { get; set; }

    /// <summary>Optimistic concurrency token, bumped on every change.</summary>
    [ConcurrencyCheck]
    public int Version { get; set; }
}

public enum MovementType
{
    Receive = 1,
    Sale = 2,
    Transfer = 3,
    Adjustment = 4,
}

/// <summary>Append-only ledger row. Stock levels are always the result of these.</summary>
public class StockMovement
{
    public int Id { get; set; }

    public MovementType Type { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int? FromLocationId { get; set; }
    public Location? FromLocation { get; set; }

    public int? ToLocationId { get; set; }
    public Location? ToLocation { get; set; }

    /// <summary>Always positive. Direction comes from From/To.</summary>
    public int Quantity { get; set; }

    [StringLength(200)]
    public string? Note { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
