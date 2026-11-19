using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inventory.Web.Data;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Tests;

/// <summary>Boots the real app (routing, MVC, antiforgery, views) on an in-memory SQLite database.</summary>
public sealed class AppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection.Open();
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:MigrateOnStartup", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<InventoryDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<InventoryDbContext>>();
            services.AddDbContext<InventoryDbContext>(o => o.UseSqlite(_connection));

            using var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            db.Database.EnsureCreated();
            SeedData.EnsureSeededAsync(db, scope.ServiceProvider.GetRequiredService<IStockService>()).GetAwaiter().GetResult();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}

public class WebAppTests(AppFactory factory) : IClassFixture<AppFactory>
{
    private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Theory]
    [InlineData("/")]
    [InlineData("/Products")]
    [InlineData("/Products/Create")]
    [InlineData("/Locations")]
    [InlineData("/Stock/Move")]
    [InlineData("/Stock/History")]
    public async Task Pages_render(string url)
    {
        var res = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.StartsWith("text/html", res.Content.Headers.ContentType!.ToString());
    }

    [Fact]
    public async Task Dashboard_lists_low_stock_from_the_seed()
    {
        var html = await _client.GetStringAsync("/");
        var start = html.IndexOf("id=\"low-stock\"", StringComparison.Ordinal);
        Assert.True(start > 0, "low-stock table missing");
        var table = html[start..html.IndexOf("</table>", start, StringComparison.Ordinal)];

        Assert.Contains("BAG-TOTE-CNV", table);       // 10 on hand, reorder at 15
        Assert.DoesNotContain("BTL-STL-750", table);  // 18 + 12 - 9 = 21, reorder at 20
        Assert.DoesNotContain("TEE-BLK-M", table);    // 168 on hand
    }

    [Fact]
    public async Task Api_search_returns_matching_products_with_totals()
    {
        var items = await _client.GetFromJsonAsync<JsonElement>("/api/products?q=TEE");
        var skus = items.EnumerateArray().Select(i => i.GetProperty("sku").GetString()).ToList();
        Assert.Equal(["TEE-BLK-M", "TEE-WHT-M"], skus);
        // 120 + 60 received, 12 sold at the store.
        Assert.Equal(168, items[0].GetProperty("onHand").GetInt32());
    }

    [Fact]
    public async Task Api_stock_breaks_down_by_location_and_404s_for_unknown_products()
    {
        var id = await ProductIdAsync("TEE-BLK-M");
        var data = await _client.GetFromJsonAsync<JsonElement>($"/api/products/{id}/stock");
        var byCode = data.GetProperty("locations").EnumerateArray()
            .ToDictionary(l => l.GetProperty("code").GetString()!, l => l.GetProperty("quantity").GetInt32());
        Assert.Equal(new Dictionary<string, int> { ["BOM-WH1"] = 60, ["PNQ-STORE"] = 18, ["PNQ-WH1"] = 90 }, byCode);

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/products/99999/stock")).StatusCode);
    }

    [Fact]
    public async Task Overselling_through_the_form_shows_an_error_and_moves_nothing()
    {
        var id = await ProductIdAsync("BAG-TOTE-CNV");
        var before = await OnHandAsync(id);

        var res = await PostFormAsync("/Stock/Move", "/Stock/Move", new()
        {
            ["Type"] = "Sale",
            ["ProductId"] = id.ToString(),
            ["FromLocationId"] = (await LocationIdAsync("PNQ-WH1")).ToString(),
            ["Quantity"] = "500",
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("can&#x27;t sell 500", await res.Content.ReadAsStringAsync());
        Assert.Equal(before, await OnHandAsync(id));
    }

    [Fact]
    public async Task Receiving_through_the_form_redirects_and_updates_stock()
    {
        var id = await ProductIdAsync("MUG-CER-350");
        var before = await OnHandAsync(id);

        var res = await PostFormAsync("/Stock/Move", "/Stock/Move", new()
        {
            ["Type"] = "Receive",
            ["ProductId"] = id.ToString(),
            ["ToLocationId"] = (await LocationIdAsync("BOM-WH1")).ToString(),
            ["Quantity"] = "15",
            ["Note"] = "PO-2201",
        });

        Assert.Equal(HttpStatusCode.Redirect, res.StatusCode);
        Assert.Equal($"/Products/Details/{id}", res.Headers.Location!.OriginalString);
        Assert.Equal(before + 15, await OnHandAsync(id));
    }

    [Fact]
    public async Task Posting_without_an_antiforgery_token_is_rejected()
    {
        var res = await _client.PostAsync("/Stock/Move", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Type"] = "Receive", ["ProductId"] = "1", ["ToLocationId"] = "1", ["Quantity"] = "1",
        }));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Duplicate_sku_is_rejected_case_insensitively()
    {
        var res = await PostFormAsync("/Products/Create", "/Products/Create", new()
        {
            ["Sku"] = "tee-blk-m",
            ["Name"] = "Duplicate",
            ["UnitPrice"] = "1",
            ["ReorderLevel"] = "0",
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("already uses this SKU", await res.Content.ReadAsStringAsync());
    }

    private async Task<HttpResponseMessage> PostFormAsync(string getUrl, string postUrl, Dictionary<string, string> fields)
    {
        var page = await _client.GetAsync(getUrl);
        var html = await page.Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "antiforgery token not found on the form");
        fields["__RequestVerificationToken"] = token;

        var req = new HttpRequestMessage(HttpMethod.Post, postUrl) { Content = new FormUrlEncodedContent(fields) };
        if (page.Headers.TryGetValues("Set-Cookie", out var cookies))
            req.Headers.Add("Cookie", cookies.Select(c => c.Split(';')[0]));
        return await _client.SendAsync(req);
    }

    private async Task<int> ProductIdAsync(string sku)
    {
        var items = await _client.GetFromJsonAsync<JsonElement>($"/api/products?q={sku}");
        return items.EnumerateArray().Single(i => i.GetProperty("sku").GetString() == sku).GetProperty("id").GetInt32();
    }

    private async Task<int> OnHandAsync(int productId) =>
        (await _client.GetFromJsonAsync<JsonElement>($"/api/products/{productId}/stock")).GetProperty("onHand").GetInt32();

    private async Task<int> LocationIdAsync(string code)
    {
        var id = await ProductIdAsync("TEE-BLK-M");
        var data = await _client.GetFromJsonAsync<JsonElement>($"/api/products/{id}/stock");
        return data.GetProperty("locations").EnumerateArray()
            .Single(l => l.GetProperty("code").GetString() == code).GetProperty("locationId").GetInt32();
    }
}
