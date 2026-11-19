using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Tests;

public class StockServiceTests : IDisposable
{
    private readonly TestDb _db = new();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly int _a, _b, _pune, _mumbai;

    public StockServiceTests() => (_a, _b, _pune, _mumbai) = _db.SeedBasics();

    public void Dispose() => _db.Dispose();

    private StockService Service(out Inventory.Web.Data.InventoryDbContext ctx)
    {
        ctx = _db.NewContext();
        return new StockService(ctx, _clock);
    }

    private int Level(int productId, int locationId)
    {
        using var ctx = _db.NewContext();
        return ctx.StockLevels.Where(s => s.ProductId == productId && s.LocationId == locationId)
            .Select(s => s.Quantity).FirstOrDefault();
    }

    [Fact]
    public async Task Receive_adds_stock_and_writes_one_ledger_row()
    {
        var svc = Service(out var ctx);

        var m = await svc.ReceiveAsync(_a, _pune, 40, "  PO-17  ");

        Assert.Equal(40, Level(_a, _pune));
        Assert.Equal(MovementType.Receive, m.Type);
        Assert.Null(m.FromLocationId);
        Assert.Equal(_pune, m.ToLocationId);
        Assert.Equal("PO-17", m.Note);
        Assert.Equal(_clock.Now.UtcDateTime, m.CreatedAtUtc);
        Assert.Equal(1, await ctx.StockMovements.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Quantities_must_be_positive(int qty)
    {
        var svc = Service(out _);
        await Assert.ThrowsAsync<StockException>(() => svc.ReceiveAsync(_a, _pune, qty));
        await Assert.ThrowsAsync<StockException>(() => svc.SellAsync(_a, _pune, qty));
        await Assert.ThrowsAsync<StockException>(() => svc.TransferAsync(_a, _pune, _mumbai, qty));
    }

    [Fact]
    public async Task Selling_more_than_on_hand_is_rejected_and_changes_nothing()
    {
        var svc = Service(out var ctx);
        await svc.ReceiveAsync(_a, _pune, 5);

        var ex = await Assert.ThrowsAsync<StockException>(() => svc.SellAsync(_a, _pune, 6));

        Assert.Contains("Only 5", ex.Message);
        Assert.Equal(5, Level(_a, _pune));
        Assert.Equal(1, await _db.NewContext().StockMovements.CountAsync());
    }

    [Fact]
    public async Task Selling_at_a_location_with_no_stock_row_is_rejected()
    {
        var svc = Service(out _);
        await Assert.ThrowsAsync<StockException>(() => svc.SellAsync(_a, _mumbai, 1));
    }

    [Fact]
    public async Task Transfer_moves_units_and_keeps_the_total()
    {
        var svc = Service(out _);
        await svc.ReceiveAsync(_a, _pune, 30);

        var m = await svc.TransferAsync(_a, _pune, _mumbai, 12, "restock");

        Assert.Equal(18, Level(_a, _pune));
        Assert.Equal(12, Level(_a, _mumbai));
        Assert.Equal((_pune, _mumbai), (m.FromLocationId!.Value, m.ToLocationId!.Value));
    }

    [Fact]
    public async Task Transfer_to_the_same_location_is_rejected()
    {
        var svc = Service(out _);
        await svc.ReceiveAsync(_a, _pune, 10);
        await Assert.ThrowsAsync<StockException>(() => svc.TransferAsync(_a, _pune, _pune, 1));
    }

    [Fact]
    public async Task Transfer_more_than_the_source_has_is_rejected()
    {
        var svc = Service(out _);
        await svc.ReceiveAsync(_a, _pune, 3);
        await Assert.ThrowsAsync<StockException>(() => svc.TransferAsync(_a, _pune, _mumbai, 4));
        Assert.Equal(3, Level(_a, _pune));
        Assert.Equal(0, Level(_a, _mumbai));
    }

    [Fact]
    public async Task Stock_take_down_logs_the_shortfall_as_an_outgoing_adjustment()
    {
        var svc = Service(out _);
        await svc.ReceiveAsync(_a, _pune, 20);

        var m = await svc.AdjustAsync(_a, _pune, 17, "3 damaged");

        Assert.Equal(17, Level(_a, _pune));
        Assert.Equal(MovementType.Adjustment, m.Type);
        Assert.Equal(3, m.Quantity);
        Assert.Equal(_pune, m.FromLocationId);
        Assert.Null(m.ToLocationId);
    }

    [Fact]
    public async Task Stock_take_up_logs_the_surplus_as_an_incoming_adjustment()
    {
        var svc = Service(out _);
        await svc.ReceiveAsync(_a, _pune, 20);

        var m = await svc.AdjustAsync(_a, _pune, 22);

        Assert.Equal(22, Level(_a, _pune));
        Assert.Equal(2, m.Quantity);
        Assert.Equal(_pune, m.ToLocationId);
    }

    [Fact]
    public async Task Stock_take_that_matches_the_record_is_rejected()
    {
        var svc = Service(out _);
        await svc.ReceiveAsync(_a, _pune, 8);
        await Assert.ThrowsAsync<StockException>(() => svc.AdjustAsync(_a, _pune, 8));
    }

    [Fact]
    public async Task Archived_products_cannot_move()
    {
        using (var ctx = _db.NewContext())
        {
            (await ctx.Products.FindAsync(_b))!.IsArchived = true;
            await ctx.SaveChangesAsync();
        }
        var svc = Service(out _);
        var ex = await Assert.ThrowsAsync<StockException>(() => svc.ReceiveAsync(_b, _pune, 1));
        Assert.Contains("archived", ex.Message);
    }

    [Fact]
    public async Task Unknown_product_or_location_is_rejected()
    {
        var svc = Service(out _);
        await Assert.ThrowsAsync<StockException>(() => svc.ReceiveAsync(9999, _pune, 1));
        await Assert.ThrowsAsync<StockException>(() => svc.ReceiveAsync(_a, 9999, 1));
    }

    [Fact]
    public async Task Low_stock_sums_all_locations_counts_missing_stock_as_zero_and_skips_archived()
    {
        var svc = Service(out _);
        await svc.ReceiveAsync(_a, _pune, 3);   // A: reorder 5 -> 3 + 2 = 5, at the level -> low
        await svc.ReceiveAsync(_a, _mumbai, 2);
        // B has no stock rows at all -> 0 on hand -> low

        var low = await svc.GetLowStockAsync();
        Assert.Equal(["B-1", "A-1"], low.Select(r => r.Sku)); // most urgent first
        Assert.Equal(5, low.Single(r => r.Sku == "A-1").OnHand);

        await svc.ReceiveAsync(_a, _pune, 1);
        using (var ctx = _db.NewContext())
        {
            (await ctx.Products.FindAsync(_b))!.IsArchived = true;
            await ctx.SaveChangesAsync();
        }
        Assert.Empty(await Service(out _).GetLowStockAsync());
    }

    [Fact]
    public async Task Ledger_always_reconciles_with_stock_levels()
    {
        var svc = Service(out _);
        var rng = new Random(42);
        int[] locations = [_pune, _mumbai];

        for (var i = 0; i < 200; i++)
        {
            var product = rng.Next(2) == 0 ? _a : _b;
            var loc = locations[rng.Next(2)];
            var other = loc == _pune ? _mumbai : _pune;
            try
            {
                switch (rng.Next(4))
                {
                    case 0: await svc.ReceiveAsync(product, loc, rng.Next(1, 20)); break;
                    case 1: await svc.SellAsync(product, loc, rng.Next(1, 15)); break;
                    case 2: await svc.TransferAsync(product, loc, other, rng.Next(1, 10)); break;
                    default: await svc.AdjustAsync(product, loc, rng.Next(0, 30)); break;
                }
            }
            catch (StockException)
            {
                // Rejected operations must leave no trace; checked below.
                svc = Service(out _); // a failed SaveChanges can leave tracked changes behind
            }
        }

        using var ctx = _db.NewContext();
        var moves = await ctx.StockMovements.ToListAsync();
        foreach (var level in await ctx.StockLevels.ToListAsync())
        {
            var inflow = moves.Where(m => m.ProductId == level.ProductId && m.ToLocationId == level.LocationId).Sum(m => m.Quantity);
            var outflow = moves.Where(m => m.ProductId == level.ProductId && m.FromLocationId == level.LocationId).Sum(m => m.Quantity);
            Assert.Equal(inflow - outflow, level.Quantity);
            Assert.True(level.Quantity >= 0);
        }
    }

    [Fact]
    public async Task Two_clerks_selling_the_same_stock_at_once_cannot_both_succeed()
    {
        await Service(out _).ReceiveAsync(_a, _pune, 5);

        // Both load the level (5) before either saves.
        var first = Service(out var ctx1);
        var second = Service(out var ctx2);
        await ctx1.StockLevels.FindAsync(_a, _pune);
        await ctx2.StockLevels.FindAsync(_a, _pune);

        await first.SellAsync(_a, _pune, 4);
        var ex = await Assert.ThrowsAsync<StockException>(() => second.SellAsync(_a, _pune, 4));

        Assert.Contains("same moment", ex.Message);
        Assert.Equal(1, Level(_a, _pune));
    }
}
