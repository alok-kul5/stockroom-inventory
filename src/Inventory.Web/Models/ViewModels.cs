using System.ComponentModel.DataAnnotations;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Inventory.Web.Models;

public class DashboardViewModel
{
    public int ProductCount { get; set; }
    public int LocationCount { get; set; }
    public int UnitsOnHand { get; set; }
    public decimal StockValue { get; set; }
    public IReadOnlyList<LowStockRow> LowStock { get; set; } = [];
    public IReadOnlyList<StockMovement> RecentMovements { get; set; } = [];
}

public class ProductListRow
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public int ReorderLevel { get; set; }
    public int OnHand { get; set; }
    public bool IsArchived { get; set; }
    public bool IsLow => !IsArchived && OnHand <= ReorderLevel;
}

public class ProductIndexViewModel
{
    public string? Search { get; set; }
    public bool ShowArchived { get; set; }
    public IReadOnlyList<ProductListRow> Rows { get; set; } = [];
}

public class ProductDetailsViewModel
{
    public Product Product { get; set; } = null!;
    public IReadOnlyList<StockMovement> Movements { get; set; } = [];
}

public class StockMoveForm
{
    [Required]
    public MovementType Type { get; set; } = MovementType.Receive;

    [Required, Display(Name = "Product")]
    public int? ProductId { get; set; }

    [Display(Name = "From location")]
    public int? FromLocationId { get; set; }

    [Display(Name = "To location")]
    public int? ToLocationId { get; set; }

    /// <summary>For Receive/Sale/Transfer: units moved. For Adjustment: the counted total.</summary>
    [Range(0, 1_000_000)]
    public int Quantity { get; set; } = 1;

    [StringLength(200)]
    public string? Note { get; set; }

    public IEnumerable<SelectListItem> Products { get; set; } = [];
    public IEnumerable<SelectListItem> Locations { get; set; } = [];
}

public class MovementHistoryViewModel
{
    public MovementType? Type { get; set; }
    public int? ProductId { get; set; }
    public int Page { get; set; } = 1;
    public int TotalPages { get; set; }
    public IReadOnlyList<StockMovement> Rows { get; set; } = [];
    public IEnumerable<SelectListItem> Products { get; set; } = [];
}
