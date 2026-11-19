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

        var model = new DashboardViewModel
        {
            ProductCount = await active.CountAsync(),
            LocationCount = await db.Locations.CountAsync(),
            UnitsOnHand = valueRows.Sum(r => r.Quantity),
            StockValue = valueRows.Sum(r => r.Quantity * r.UnitPrice),
            LowStock = await stock.GetLowStockAsync(),
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
