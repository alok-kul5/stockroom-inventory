using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Data;

/// <summary>Fills an empty database with a small, believable shop so the app has something to show.</summary>
public static class SeedData
{
    public static async Task EnsureSeededAsync(InventoryDbContext db, IStockService stock)
    {
        if (await db.Products.AnyAsync()) return;

        var pune = new Location { Code = "PNQ-WH1", Name = "Pune Warehouse" };
        var mumbai = new Location { Code = "BOM-WH1", Name = "Mumbai Warehouse" };
        var store = new Location { Code = "PNQ-STORE", Name = "Pune Retail Store" };
        db.Locations.AddRange(pune, mumbai, store);

        var products = new[]
        {
            new Product { Sku = "TEE-BLK-M", Name = "Cotton T-shirt, black, M", UnitPrice = 499, ReorderLevel = 40 },
            new Product { Sku = "TEE-WHT-M", Name = "Cotton T-shirt, white, M", UnitPrice = 499, ReorderLevel = 40 },
            new Product { Sku = "MUG-CER-350", Name = "Ceramic mug, 350 ml", UnitPrice = 299, ReorderLevel = 25 },
            new Product { Sku = "BTL-STL-750", Name = "Steel water bottle, 750 ml", UnitPrice = 749, ReorderLevel = 20 },
            new Product { Sku = "NB-A5-DOT", Name = "A5 dotted notebook", UnitPrice = 199, ReorderLevel = 60 },
            new Product { Sku = "BAG-TOTE-CNV", Name = "Canvas tote bag", UnitPrice = 349, ReorderLevel = 15 },
        };
        db.Products.AddRange(products);
        await db.SaveChangesAsync();

        // Build the starting stock through the service so the ledger explains every number.
        int[] pnq = [120, 35, 80, 18, 200, 10];
        int[] bom = [60, 20, 40, 12, 90, 0];
        for (var i = 0; i < products.Length; i++)
        {
            await stock.ReceiveAsync(products[i].Id, pune.Id, pnq[i], "Opening stock");
            if (bom[i] > 0) await stock.ReceiveAsync(products[i].Id, mumbai.Id, bom[i], "Opening stock");
        }

        await stock.TransferAsync(products[0].Id, pune.Id, store.Id, 30, "Store restock");
        await stock.TransferAsync(products[2].Id, pune.Id, store.Id, 20, "Store restock");
        await stock.SellAsync(products[0].Id, store.Id, 12);
        await stock.SellAsync(products[2].Id, store.Id, 7);
        await stock.SellAsync(products[3].Id, pune.Id, 9, "Online order #1042");
        await stock.AdjustAsync(products[4].Id, pune.Id, 196, "Stock-take: 4 damaged");
    }
}
