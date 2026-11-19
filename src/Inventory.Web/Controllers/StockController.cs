using Inventory.Web.Data;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Controllers;

public class StockController(InventoryDbContext db, IStockService stock) : Controller
{
    private const int PageSize = 20;

    public async Task<IActionResult> Move(MovementType type = MovementType.Receive, int? productId = null)
    {
        var form = new StockMoveForm { Type = type, ProductId = productId };
        await FillListsAsync(form);
        return View(form);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Move(StockMoveForm form)
    {
        if (ModelState.IsValid)
        {
            try
            {
                var productId = form.ProductId!.Value;
                var movement = form.Type switch
                {
                    MovementType.Receive => await stock.ReceiveAsync(productId, Need(form.ToLocationId, "to"), form.Quantity, form.Note),
                    MovementType.Sale => await stock.SellAsync(productId, Need(form.FromLocationId, "from"), form.Quantity, form.Note),
                    MovementType.Transfer => await stock.TransferAsync(productId, Need(form.FromLocationId, "from"), Need(form.ToLocationId, "to"), form.Quantity, form.Note),
                    MovementType.Adjustment => await stock.AdjustAsync(productId, Need(form.ToLocationId ?? form.FromLocationId, "the"), form.Quantity, form.Note),
                    _ => throw new StockException("Unknown movement type."),
                };
                TempData["Flash"] = $"Recorded {movement.Type.ToString().ToLowerInvariant()} of {movement.Quantity}.";
                return RedirectToAction("Details", "Products", new { id = productId });
            }
            catch (StockException ex)
            {
                ModelState.AddModelError("", ex.Message);
            }
        }

        await FillListsAsync(form);
        return View(form);
    }

    public async Task<IActionResult> History(MovementType? type, int? productId, int page = 1)
    {
        var query = db.StockMovements.AsNoTracking();
        if (type is not null) query = query.Where(m => m.Type == type);
        if (productId is not null) query = query.Where(m => m.ProductId == productId);

        var total = await query.CountAsync();
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, totalPages);

        var rows = await query
            .Include(m => m.Product).Include(m => m.FromLocation).Include(m => m.ToLocation)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Skip((page - 1) * PageSize).Take(PageSize)
            .ToListAsync();

        return View(new MovementHistoryViewModel
        {
            Type = type,
            ProductId = productId,
            Page = page,
            TotalPages = totalPages,
            Rows = rows,
            Products = await ProductOptionsAsync(),
        });
    }

    private static int Need(int? locationId, string which) =>
        locationId ?? throw new StockException($"Pick {(which == "the" ? "a" : "the " + which)} location.");

    private async Task FillListsAsync(StockMoveForm form)
    {
        form.Products = await ProductOptionsAsync();
        form.Locations = await db.Locations.AsNoTracking().OrderBy(l => l.Code)
            .Select(l => new SelectListItem($"{l.Code} · {l.Name}", l.Id.ToString()))
            .ToListAsync();
    }

    private async Task<List<SelectListItem>> ProductOptionsAsync() =>
        await db.Products.AsNoTracking().Where(p => !p.IsArchived).OrderBy(p => p.Sku)
            .Select(p => new SelectListItem($"{p.Sku} · {p.Name}", p.Id.ToString()))
            .ToListAsync();
}
