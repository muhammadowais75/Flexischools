using FluentAssertions;
using Flexischools.Api.Domain.Entities;
using Flexischools.Api.Domain.Enums;
using Flexischools.Api.Domain.Exceptions;
using NUnit.Framework;

namespace Flexischools.Tests.Unit.Domain;

[TestFixture]
public class OrderDomainTests
{
    private Parent _parent = null!;
    private Student _student = null!;
    private Canteen _canteen = null!;
    private MenuItem _sandwich = null!;

    // A fixed "Monday 9:00 AM AEST" used as the creation time for most tests.
    // Cut-off is 9:30 AM, so orders placed at 9:00 AM should be allowed.
    private DateTimeOffset _beforeCutOff;
    private DateOnly _fulfilmentDate;

    [SetUp]
    public void SetUp()
    {
        _parent = Parent.Create("Jane Smith", "jane@test.com", 50m);
        _student = Student.Create("Tom Smith", _parent.Id);
        _canteen = Canteen.Create(
            "Test Canteen",
            new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                    DayOfWeek.Thursday, DayOfWeek.Friday },
            new TimeSpan(9, 30, 0));
        _sandwich = MenuItem.Create("Sandwich", 5m, _canteen.Id, dailyStock: 10);

        // Find next Monday
        var monday = DateTime.Today;
        while (monday.DayOfWeek != DayOfWeek.Monday) monday = monday.AddDays(1);
        _fulfilmentDate = DateOnly.FromDateTime(monday);

        // AEST is UTC+10
        _beforeCutOff = new DateTimeOffset(monday.Year, monday.Month, monday.Day, 9, 0, 0,
            TimeSpan.FromHours(10));
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Test]
    public void CreateOrder_ValidRequest_PlacesOrderAndDebitsWalletAndStock()
    {
        var order = Order.Create(_parent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 2) }, _beforeCutOff);

        order.Status.Should().Be(OrderStatus.Placed);
        order.TotalAmount.Should().Be(10m);
        order.Items.Should().HaveCount(1);
        _parent.WalletBalance.Should().Be(40m, "wallet should be debited by order total");
        _sandwich.DailyStock.Should().Be(8, "stock should be decremented by quantity");
    }

    [Test]
    public void CreateOrder_MultipleLineItems_CalculatesTotalCorrectly()
    {
        var juice = MenuItem.Create("Juice", 3m, _canteen.Id, dailyStock: 5);

        var order = Order.Create(_parent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 1), (juice, 2) }, _beforeCutOff);

        order.TotalAmount.Should().Be(11m); // 5 + 3*2
        _parent.WalletBalance.Should().Be(39m);
    }

    [Test]
    public void CreateOrder_UnlimitedStockItem_DoesNotDecrementStock()
    {
        var juice = MenuItem.Create("Unlimited Juice", 2m, _canteen.Id); // no dailyStock
        var parent = Parent.Create("Rich Parent", "rich@test.com", 100m);

        var order = Order.Create(parent, _student, _canteen, _fulfilmentDate,
            new[] { (juice, 5) }, _beforeCutOff);

        juice.DailyStock.Should().BeNull("unlimited stock items should stay null");
        order.TotalAmount.Should().Be(10m);
    }

    // ── Cut-off rule ──────────────────────────────────────────────────────────

    [Test]
    public void CreateOrder_ExactlyAtCutOff_IsRejected()
    {
        // Cut-off is 09:30; an order placed at exactly 09:30 should be rejected (not strictly before)
        var monday = _fulfilmentDate.ToDateTime(TimeOnly.MinValue);
        var atCutOff = new DateTimeOffset(monday.Year, monday.Month, monday.Day, 9, 30, 0,
            TimeSpan.FromHours(10));

        var act = () => Order.Create(_parent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 1) }, atCutOff);

        act.Should().Throw<OrderCutOffException>()
            .WithMessage("*9:30*");
    }

    [Test]
    public void CreateOrder_AfterCutOff_IsRejected()
    {
        var monday = _fulfilmentDate.ToDateTime(TimeOnly.MinValue);
        var afterCutOff = new DateTimeOffset(monday.Year, monday.Month, monday.Day, 11, 0, 0,
            TimeSpan.FromHours(10));

        var act = () => Order.Create(_parent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 1) }, afterCutOff);

        act.Should().Throw<OrderCutOffException>();
    }

    [Test]
    public void CreateOrder_WeekendDay_IsRejected()
    {
        var saturday = _fulfilmentDate.ToDateTime(TimeOnly.MinValue);
        while (saturday.DayOfWeek != DayOfWeek.Saturday) saturday = saturday.AddDays(1);
        var saturdayDate = DateOnly.FromDateTime(saturday);
        var saturdayNow = new DateTimeOffset(saturday.Year, saturday.Month, saturday.Day, 8, 0, 0,
            TimeSpan.FromHours(10));

        var act = () => Order.Create(_parent, _student, _canteen, saturdayDate,
            new[] { (_sandwich, 1) }, saturdayNow);

        act.Should().Throw<OrderCutOffException>();
    }

    // ── Stock rule ────────────────────────────────────────────────────────────

    [Test]
    public void CreateOrder_RequestMoreThanStock_IsRejected()
    {
        var act = () => Order.Create(_parent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 999) }, _beforeCutOff);

        act.Should().Throw<InsufficientStockException>()
            .WithMessage("*Sandwich*");
    }

    [Test]
    public void CreateOrder_ExactlyAvailableStock_IsAllowed()
    {
        var parent = Parent.Create("Rich", "r@test.com", 500m);

        // DailyStock = 10, request 10 — should succeed
        var order = Order.Create(parent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 10) }, _beforeCutOff);

        order.Should().NotBeNull();
        _sandwich.DailyStock.Should().Be(0);
    }

    // ── Wallet rule ───────────────────────────────────────────────────────────

    [Test]
    public void CreateOrder_InsufficientWalletBalance_IsRejected()
    {
        var brokeParent = Parent.Create("Broke Parent", "broke@test.com", 1m);

        var act = () => Order.Create(brokeParent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 1) }, _beforeCutOff);

        act.Should().Throw<InsufficientWalletBalanceException>()
            .WithMessage("*$1.00*");
    }

    [Test]
    public void CreateOrder_ExactWalletBalance_IsAllowed()
    {
        var exactParent = Parent.Create("Exact", "exact@test.com", 5m);

        var order = Order.Create(exactParent, _student, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 1) }, _beforeCutOff);

        order.TotalAmount.Should().Be(5m);
        exactParent.WalletBalance.Should().Be(0m);
    }

    // ── Allergen rule ─────────────────────────────────────────────────────────

    [Test]
    public void CreateOrder_StudentHasMatchingAllergen_IsRejected()
    {
        var allergyStudent = Student.Create("Allergic Kid", _parent.Id, new[] { "nuts" });
        var nutBar = MenuItem.Create("Nut Bar", 2m, _canteen.Id,
            allergenTags: new[] { "nuts" });

        var act = () => Order.Create(_parent, allergyStudent, _canteen, _fulfilmentDate,
            new[] { (nutBar, 1) }, _beforeCutOff);

        act.Should().Throw<AllergenConflictException>()
            .WithMessage("*nuts*");
    }

    [Test]
    public void CreateOrder_StudentAllergenButSafeItem_IsAllowed()
    {
        var allergyStudent = Student.Create("Allergic Kid", _parent.Id, new[] { "nuts" });
        // Sandwich has no allergens — should be fine
        var order = Order.Create(_parent, allergyStudent, _canteen, _fulfilmentDate,
            new[] { (_sandwich, 1) }, _beforeCutOff);

        order.Should().NotBeNull();
    }

    [Test]
    public void CreateOrder_StudentAllergenCaseInsensitive_IsRejected()
    {
        var allergyStudent = Student.Create("Kid", _parent.Id, new[] { "NUTS" });
        var nutBar = MenuItem.Create("Nut Bar", 2m, _canteen.Id, allergenTags: new[] { "nuts" });

        var act = () => Order.Create(_parent, allergyStudent, _canteen, _fulfilmentDate,
            new[] { (nutBar, 1) }, _beforeCutOff);

        act.Should().Throw<AllergenConflictException>();
    }

    // ── Wallet methods ────────────────────────────────────────────────────────

    [Test]
    public void DebitWallet_MoreThanBalance_Throws()
    {
        var act = () => _parent.DebitWallet(999m);
        act.Should().Throw<InsufficientWalletBalanceException>();
    }

    [Test]
    public void CreditWallet_AddsToBalance()
    {
        _parent.CreditWallet(10m);
        _parent.WalletBalance.Should().Be(60m);
    }

    [Test]
    public void DebitWallet_ZeroOrNegativeAmount_Throws()
    {
        var act = () => _parent.DebitWallet(0m);
        act.Should().Throw<ArgumentException>();
    }

    // ── MenuItem stock methods ────────────────────────────────────────────────

    [Test]
    public void TryDeductStock_ExceedsAvailable_ReturnsFalse()
    {
        var result = _sandwich.TryDeductStock(999);
        result.Should().BeFalse();
        _sandwich.DailyStock.Should().Be(10, "stock should not change on failure");
    }

    [Test]
    public void RestoreStock_IncreasesStockByQuantity()
    {
        _sandwich.TryDeductStock(3);
        _sandwich.RestoreStock(3);
        _sandwich.DailyStock.Should().Be(10);
    }
}
