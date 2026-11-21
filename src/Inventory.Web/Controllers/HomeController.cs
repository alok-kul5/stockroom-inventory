using System.Diagnostics;
using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Controllers;

public class HomeController(InventoryDbContext db, IStockService stock) : Controller
{
    public async Task<IActionResult> Index()
    {
        var active = db.Products.AsNoTracking().Where(p => !p.IsArchived);

        // Stock value is summed in memory: SQLite (used in tests) can't aggregate decimals.
        var valueRows = await db.StockLevels.AsNoTracking()
            .Where(s => !s.Product.IsArchived)
            .Select(s => new { s.Quantity, s.Product.UnitPrice })
            .ToListAsync();

        var lowStock = await stock.GetLowStockAsync();
        var lowIds = lowStock.Select(l => l.ProductId).ToHashSet();
        var levels = await db.StockLevels.AsNoTracking()
            .Where(s => !s.Product.IsArchived && s.Quantity > 0)
            .Select(s => new { s.LocationId, s.ProductId, s.Product.Sku, s.Product.Name, s.Product.UnitPrice, s.Quantity })
            .ToListAsync();
        var locations = await db.Locations.AsNoTracking().OrderBy(l => l.Code).ToListAsync();
        var shelves = locations.Select(l =>
        {
            var here = levels.Where(s => s.LocationId == l.Id).OrderByDescending(s => s.Quantity).ToList();
            return new ShelfRow(l.Id, l.Code, l.Name, here.Sum(s => s.Quantity), here.Sum(s => s.Quantity * s.UnitPrice),
                here.Select(s => new ShelfItem(s.ProductId, s.Sku, s.Name, s.Quantity, lowIds.Contains(s.ProductId))).ToList());
        }).ToList();

        var model = new DashboardViewModel
        {
            ProductCount = await active.CountAsync(),
            LocationCount = await db.Locations.CountAsync(),
            UnitsOnHand = valueRows.Sum(r => r.Quantity),
            StockValue = valueRows.Sum(r => r.Quantity * r.UnitPrice),
            LowStock = lowStock,
            Shelves = shelves,
            RecentMovements = await db.StockMovements.AsNoTracking()
                .Include(m => m.Product).Include(m => m.FromLocation).Include(m => m.ToLocation)
                .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
                .Take(8)
                .ToListAsync(),
        };
        return View(model);
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
}
