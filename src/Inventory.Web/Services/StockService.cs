using Inventory.Web.Data;
using Inventory.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Services;

/// <summary>Thrown when a stock operation is invalid (bad quantity, not enough stock, unknown ids).</summary>
public class StockException(string message) : Exception(message);

public record LowStockRow(int ProductId, string Sku, string Name, int OnHand, int ReorderLevel);

public interface IStockService
{
    Task<StockMovement> ReceiveAsync(int productId, int locationId, int quantity, string? note = null);
    Task<StockMovement> SellAsync(int productId, int locationId, int quantity, string? note = null);
    Task<StockMovement> TransferAsync(int productId, int fromLocationId, int toLocationId, int quantity, string? note = null);
    Task<StockMovement> AdjustAsync(int productId, int locationId, int countedQuantity, string? note = null);
    Task<IReadOnlyList<LowStockRow>> GetLowStockAsync();
}

/// <summary>
/// Every stock change goes through here: it writes one ledger row and updates the affected
/// stock levels in a single SaveChanges, so the two can never disagree.
/// </summary>
public class StockService(InventoryDbContext db, TimeProvider clock) : IStockService
{
    public async Task<StockMovement> ReceiveAsync(int productId, int locationId, int quantity, string? note = null)
    {
        RequirePositive(quantity);
        await RequireActiveProductAsync(productId);
        await RequireLocationAsync(locationId);

        var level = await GetOrCreateLevelAsync(productId, locationId);
        level.Quantity += quantity;
        level.Version++;

        return await RecordAsync(MovementType.Receive, productId, null, locationId, quantity, note);
    }

    public async Task<StockMovement> SellAsync(int productId, int locationId, int quantity, string? note = null)
    {
        RequirePositive(quantity);
        await RequireActiveProductAsync(productId);
        await RequireLocationAsync(locationId);

        var level = await GetOrCreateLevelAsync(productId, locationId);
        if (level.Quantity < quantity)
            throw new StockException($"Only {level.Quantity} in stock at this location, can't sell {quantity}.");
        level.Quantity -= quantity;
        level.Version++;

        return await RecordAsync(MovementType.Sale, productId, locationId, null, quantity, note);
    }

    public async Task<StockMovement> TransferAsync(int productId, int fromLocationId, int toLocationId, int quantity, string? note = null)
    {
        RequirePositive(quantity);
        if (fromLocationId == toLocationId)
            throw new StockException("Pick two different locations for a transfer.");
        await RequireActiveProductAsync(productId);
        await RequireLocationAsync(fromLocationId);
        await RequireLocationAsync(toLocationId);

        var from = await GetOrCreateLevelAsync(productId, fromLocationId);
        if (from.Quantity < quantity)
            throw new StockException($"Only {from.Quantity} at the source location, can't move {quantity}.");
        var to = await GetOrCreateLevelAsync(productId, toLocationId);

        from.Quantity -= quantity;
        from.Version++;
        to.Quantity += quantity;
        to.Version++;

        return await RecordAsync(MovementType.Transfer, productId, fromLocationId, toLocationId, quantity, note);
    }

    /// <summary>Stock-take: sets the level to what was physically counted and logs the difference.</summary>
    public async Task<StockMovement> AdjustAsync(int productId, int locationId, int countedQuantity, string? note = null)
    {
        if (countedQuantity < 0) throw new StockException("A counted quantity can't be negative.");
        await RequireActiveProductAsync(productId);
        await RequireLocationAsync(locationId);

        var level = await GetOrCreateLevelAsync(productId, locationId);
        var diff = countedQuantity - level.Quantity;
        if (diff == 0) throw new StockException("The count matches what's on record, nothing to adjust.");

        level.Quantity = countedQuantity;
        level.Version++;

        return diff > 0
            ? await RecordAsync(MovementType.Adjustment, productId, null, locationId, diff, note)
            : await RecordAsync(MovementType.Adjustment, productId, locationId, null, -diff, note);
    }

    public async Task<IReadOnlyList<LowStockRow>> GetLowStockAsync()
    {
        // Products with no stock rows at all count as 0 on hand.
        var rows = await db.Products
            .Where(p => !p.IsArchived)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                p.ReorderLevel,
                OnHand = p.StockLevels.Sum(s => (int?)s.Quantity) ?? 0,
            })
            .Where(x => x.OnHand <= x.ReorderLevel)
            .OrderBy(x => x.OnHand - x.ReorderLevel)
            .ThenBy(x => x.Sku)
            .ToListAsync();

        return rows.Select(x => new LowStockRow(x.Id, x.Sku, x.Name, x.OnHand, x.ReorderLevel)).ToList();
    }

    private async Task<StockMovement> RecordAsync(MovementType type, int productId, int? fromId, int? toId, int quantity, string? note)
    {
        var movement = new StockMovement
        {
            Type = type,
            ProductId = productId,
            FromLocationId = fromId,
            ToLocationId = toId,
            Quantity = quantity,
            Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.StockMovements.Add(movement);
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new StockException("Someone else changed this stock at the same moment. Reload and try again.");
        }
        return movement;
    }

    private async Task<StockLevel> GetOrCreateLevelAsync(int productId, int locationId)
    {
        var level = await db.StockLevels.FindAsync(productId, locationId);
        if (level is not null) return level;
        level = new StockLevel { ProductId = productId, LocationId = locationId };
        db.StockLevels.Add(level);
        return level;
    }

    private static void RequirePositive(int quantity)
    {
        if (quantity <= 0) throw new StockException("Quantity must be at least 1.");
    }

    private async Task RequireActiveProductAsync(int productId)
    {
        var product = await db.Products.FindAsync(productId) ?? throw new StockException("That product doesn't exist.");
        if (product.IsArchived) throw new StockException($"{product.Sku} is archived, restore it before moving stock.");
    }

    private async Task RequireLocationAsync(int locationId)
    {
        if (await db.Locations.FindAsync(locationId) is null) throw new StockException("That location doesn't exist.");
    }
}
