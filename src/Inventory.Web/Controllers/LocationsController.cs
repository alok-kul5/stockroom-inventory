using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Controllers;

public class LocationsController(InventoryDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        var rows = await db.Locations.AsNoTracking()
            .OrderBy(l => l.Code)
            .Select(l => new LocationRow(l.Id, l.Code, l.Name,
                l.StockLevels.Count(s => s.Quantity > 0),
                l.StockLevels.Sum(s => (int?)s.Quantity) ?? 0))
            .ToListAsync();
        return View(rows);
    }

    public IActionResult Create() => View(new Location());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Code,Name")] Location location)
    {
        location.Code = location.Code?.Trim().ToUpperInvariant() ?? "";
        ModelState.Remove(nameof(Location.Code));
        TryValidateModel(location);
        if (await db.Locations.AnyAsync(l => l.Code == location.Code))
            ModelState.AddModelError(nameof(Location.Code), "Another location already uses this code.");
        if (!ModelState.IsValid) return View(location);

        db.Locations.Add(location);
        await db.SaveChangesAsync();
        TempData["Flash"] = $"Added location {location.Code}.";
        return RedirectToAction(nameof(Index));
    }
}

public record LocationRow(int Id, string Code, string Name, int ProductCount, int Units);
