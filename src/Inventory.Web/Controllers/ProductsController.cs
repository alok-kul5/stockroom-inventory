using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Controllers;

public class ProductsController(InventoryDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search, bool showArchived = false)
    {
        var query = db.Products.AsNoTracking();
        if (!showArchived) query = query.Where(p => !p.IsArchived);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Sku.Contains(term) || p.Name.Contains(term));
        }

        var rows = await query
            .OrderBy(p => p.Sku)
            .Select(p => new ProductListRow
            {
                Id = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                UnitPrice = p.UnitPrice,
                ReorderLevel = p.ReorderLevel,
                OnHand = p.StockLevels.Sum(s => (int?)s.Quantity) ?? 0,
                IsArchived = p.IsArchived,
            })
            .ToListAsync();

        return View(new ProductIndexViewModel { Search = search, ShowArchived = showArchived, Rows = rows });
    }

    public async Task<IActionResult> Details(int id)
    {
        var product = await db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        var movements = await db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == id)
            .Include(m => m.FromLocation).Include(m => m.ToLocation)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
            .Take(25)
            .ToListAsync();

        return View(new ProductDetailsViewModel { Product = product, Movements = movements });
    }

    public IActionResult Create() => View(new Product());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Sku,Name,Description,UnitPrice,ReorderLevel")] Product product)
    {
        product.Sku = product.Sku?.Trim().ToUpperInvariant() ?? "";
        ModelState.Remove(nameof(Product.Sku));
        TryValidateModel(product);
        if (await db.Products.AnyAsync(p => p.Sku == product.Sku))
            ModelState.AddModelError(nameof(Product.Sku), "Another product already uses this SKU.");
        if (!ModelState.IsValid) return View(product);

        db.Products.Add(product);
        await db.SaveChangesAsync();
        TempData["Flash"] = $"Added {product.Sku}.";
        return RedirectToAction(nameof(Details), new { id = product.Id });
    }

    public async Task<IActionResult> Edit(int id)
    {
        var product = await db.Products.FindAsync(id);
        return product is null ? NotFound() : View(product);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Name,Description,UnitPrice,ReorderLevel")] Product form)
    {
        var product = await db.Products.FindAsync(id);
        if (product is null) return NotFound();

        // SKU is fixed once created: it is the key people and other systems refer to.
        ModelState.Remove(nameof(Product.Sku));
        if (!ModelState.IsValid)
        {
            form.Id = id;
            form.Sku = product.Sku;
            return View(form);
        }

        product.Name = form.Name;
        product.Description = form.Description;
        product.UnitPrice = form.UnitPrice;
        product.ReorderLevel = form.ReorderLevel;
        await db.SaveChangesAsync();
        TempData["Flash"] = $"Saved {product.Sku}.";
        return RedirectToAction(nameof(Details), new { id });
    }

    /// <summary>Archive instead of delete: the stock ledger keeps pointing at the product.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleArchive(int id)
    {
        var product = await db.Products.Include(p => p.StockLevels).FirstOrDefaultAsync(p => p.Id == id);
        if (product is null) return NotFound();

        if (!product.IsArchived && product.StockLevels.Any(s => s.Quantity > 0))
        {
            TempData["Error"] = $"{product.Sku} still has stock. Sell, transfer or adjust it to zero before archiving.";
            return RedirectToAction(nameof(Details), new { id });
        }

        product.IsArchived = !product.IsArchived;
        await db.SaveChangesAsync();
        TempData["Flash"] = product.IsArchived ? $"Archived {product.Sku}." : $"Restored {product.Sku}.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
