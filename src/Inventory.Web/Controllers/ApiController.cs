using Inventory.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Controllers;

/// <summary>Small JSON API used by the pages' JavaScript (product search, live stock per location).</summary>
[ApiController]
[Route("api")]
public class ApiController(InventoryDbContext db) : ControllerBase
{
    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] string? q, [FromQuery] int take = 20)
    {
        take = Math.Clamp(take, 1, 100);
        var query = db.Products.AsNoTracking().Where(p => !p.IsArchived);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            query = query.Where(p => p.Sku.Contains(term) || p.Name.Contains(term));
        }

        var items = await query
            .OrderBy(p => p.Sku)
            .Take(take)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                p.UnitPrice,
                p.ReorderLevel,
                OnHand = p.StockLevels.Sum(s => (int?)s.Quantity) ?? 0,
            })
            .ToListAsync();
        return Ok(items);
    }

    [HttpGet("products/{id:int}/stock")]
    public async Task<IActionResult> Stock(int id)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound(new { error = "Product not found." });

        var levels = await db.Locations.AsNoTracking()
            .OrderBy(l => l.Code)
            .Select(l => new
            {
                LocationId = l.Id,
                l.Code,
                l.Name,
                Quantity = l.StockLevels.Where(s => s.ProductId == id).Sum(s => (int?)s.Quantity) ?? 0,
            })
            .ToListAsync();

        return Ok(new
        {
            product.Id,
            product.Sku,
            product.Name,
            product.ReorderLevel,
            OnHand = levels.Sum(l => l.Quantity),
            Locations = levels,
        });
    }
}
