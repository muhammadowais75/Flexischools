using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Flexischools.Api.Application.Orders.Commands;
using Flexischools.Api.Application.Orders.Queries;
using Flexischools.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Flexischools.Tests.Integration;

/// <summary>
/// Integration tests that exercise the full HTTP → handler → SQLite path.
/// Each test class gets its own in-memory SQLite database to stay isolated and deterministic.
/// </summary>
[TestFixture]
public class OrdersApiTests
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _dbName = null!;

    [SetUp]
    public void SetUp()
    {
        // Unique DB name per test run so tests never share state
        _dbName = $"flexischools_test_{Guid.NewGuid():N}";

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.ConfigureServices(services =>
                {
                    // Swap out real SQLite file DB for an in-memory SQLite DB
                    var descriptor = services.SingleOrDefault(d =>
                        d.ServiceType == typeof(DbContextOptions<AppDbContext>));
                    if (descriptor != null) services.Remove(descriptor);

                    services.AddDbContext<AppDbContext>(opts =>
                        opts.UseSqlite($"Data Source={_dbName};Mode=Memory;Cache=Shared"));
                });
            });

        _client = _factory.CreateClient();

        // Initialise schema and seed data
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        DatabaseSeeder.SeedAsync(db).GetAwaiter().GetResult();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Test]
    public async Task PostOrder_ValidRequest_Returns201WithOrderId()
    {
        var ids = await GetSeedIds();
        var response = await _client.PostAsJsonAsync("/orders", BuildRequest(ids));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<CreateOrderResult>();
        body!.OrderId.Should().NotBeEmpty();
        body.Status.Should().Be("Placed");
        body.Total.Should().BeGreaterThan(0);

        // Location header should point to GET /orders/{id}
        response.Headers.Location.Should().NotBeNull();
    }

    [Test]
    public async Task GetOrder_ExistingOrder_Returns200WithDetails()
    {
        var ids = await GetSeedIds();
        var orderId = await CreateOrder(ids);

        var response = await _client.GetAsync($"/orders/{orderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OrderDetailDto>();
        body!.Id.Should().Be(orderId);
        body.Status.Should().Be("Placed");
        body.Items.Should().NotBeEmpty();
    }

    // ── Idempotency ───────────────────────────────────────────────────────────

    [Test]
    public async Task PostOrder_SameIdempotencyKey_ReturnsSameOrderBothTimes()
    {
        var ids = await GetSeedIds();
        var key = $"idem-{Guid.NewGuid()}";

        var resp1 = await SendWithIdempotencyKey(ids, key);
        var resp2 = await SendWithIdempotencyKey(ids, key);

        resp1.StatusCode.Should().Be(HttpStatusCode.Created);
        resp2.StatusCode.Should().Be(HttpStatusCode.Created);

        var body1 = await resp1.Content.ReadFromJsonAsync<CreateOrderResult>();
        var body2 = await resp2.Content.ReadFromJsonAsync<CreateOrderResult>();

        body1!.OrderId.Should().Be(body2!.OrderId,
            "same idempotency key must return the same order ID");
    }

    [Test]
    public async Task PostOrder_DifferentIdempotencyKeys_CreateSeparateOrders()
    {
        var ids = await GetSeedIds();

        // Top up wallet so parent can afford two orders
        await TopUpParentWallet(ids.ParentId, 200m);

        var resp1 = await SendWithIdempotencyKey(ids, $"key-{Guid.NewGuid()}");
        var resp2 = await SendWithIdempotencyKey(ids, $"key-{Guid.NewGuid()}");

        var body1 = await resp1.Content.ReadFromJsonAsync<CreateOrderResult>();
        var body2 = await resp2.Content.ReadFromJsonAsync<CreateOrderResult>();

        body1!.OrderId.Should().NotBe(body2!.OrderId);
    }

    // ── Business rule rejection scenarios ─────────────────────────────────────

    [Test]
    public async Task PostOrder_NonExistentParent_Returns404()
    {
        var ids = await GetSeedIds();
        var request = BuildRequest(ids) with { ParentId = Guid.NewGuid() };

        var response = await _client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PostOrder_NonExistentMenuItem_Returns404()
    {
        var ids = await GetSeedIds();
        var request = BuildRequest(ids) with
        {
            Lines = new List<OrderLineRequest> { new(Guid.NewGuid(), 1) }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Test]
    public async Task PostOrder_InsufficientWallet_Returns422()
    {
        var ids = await GetSeedIds();
        // Drain wallet first
        await DrainParentWallet(ids.ParentId);

        var response = await _client.PostAsJsonAsync("/orders", BuildRequest(ids));

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Wallet");
    }

    [Test]
    public async Task PostOrder_AllergenConflict_Returns422()
    {
        var ids = await GetSeedIds();
        // The seeded student (Tom) has allergen "nuts". The nut bar has allergen "nuts".
        var nutBarId = await GetNutBarId();
        var request = BuildRequest(ids) with
        {
            Lines = new List<OrderLineRequest> { new(nutBarId, 1) }
        };

        var response = await _client.PostAsJsonAsync("/orders", request);

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("allergen", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task GetOrder_NonExistentId_Returns404()
    {
        var response = await _client.GetAsync($"/orders/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<SeedIds> GetSeedIds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parent = await db.Parents.FirstAsync();
        var student = await db.Students.FirstAsync();
        var canteen = await db.Canteens.FirstAsync();
        var sandwich = await db.MenuItems.FirstAsync(m => m.Name == "Vegemite Sandwich");
        return new SeedIds(parent.Id, student.Id, canteen.Id, sandwich.Id);
    }

    private async Task<Guid> GetNutBarId()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var nutBar = await db.MenuItems.FirstAsync(m => m.Name == "Nut Bar");
        return nutBar.Id;
    }

    private async Task<Guid> CreateOrder(SeedIds ids)
    {
        var response = await _client.PostAsJsonAsync("/orders", BuildRequest(ids));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<CreateOrderResult>();
        return body!.OrderId;
    }

    private async Task<HttpResponseMessage> SendWithIdempotencyKey(SeedIds ids, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders");
        request.Headers.Add("Idempotency-Key", key);
        request.Content = JsonContent.Create(BuildRequest(ids));
        return await _client.SendAsync(request);
    }

    private async Task TopUpParentWallet(Guid parentId, decimal amount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parent = await db.Parents.FindAsync(parentId);
        parent!.CreditWallet(amount);
        await db.SaveChangesAsync();
    }

    private async Task DrainParentWallet(Guid parentId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parent = await db.Parents.FindAsync(parentId);
        // Use EF's shadow property access to zero out balance for test setup
        // (bypasses domain rules intentionally — this is a test helper only)
        db.Entry(parent!).Property("WalletBalance").CurrentValue = 0m;
        await db.SaveChangesAsync();
    }

    private static CreateOrderHttpRequest BuildRequest(SeedIds ids) => new(
        ids.ParentId,
        ids.StudentId,
        ids.CanteenId,
        GetNextWeekday(DayOfWeek.Monday),
        new List<OrderLineRequest> { new(ids.SandwichId, 1) }
    );

    private static DateOnly GetNextWeekday(DayOfWeek day)
    {
        var date = DateTime.Today;
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return DateOnly.FromDateTime(date);
    }

    // ── Local request/response types ──────────────────────────────────────────

    private record SeedIds(Guid ParentId, Guid StudentId, Guid CanteenId, Guid SandwichId);

    private record CreateOrderHttpRequest(
        Guid ParentId,
        Guid StudentId,
        Guid CanteenId,
        DateOnly FulfilmentDate,
        IReadOnlyList<OrderLineRequest> Lines
    );

    private record OrderLineRequest(Guid MenuItemId, int Quantity);
}
