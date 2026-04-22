using Flexischools.Api.Domain.Enums;
using Flexischools.Api.Domain.Exceptions;

namespace Flexischools.Api.Domain.Entities;

public class Order
{
    public Guid Id { get; private set; }
    public Guid ParentId { get; private set; }
    public Parent Parent { get; private set; } = null!;

    public Guid StudentId { get; private set; }
    public Student Student { get; private set; } = null!;

    public Guid CanteenId { get; private set; }
    public Canteen Canteen { get; private set; } = null!;

    public DateOnly FulfilmentDate { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal TotalAmount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public ICollection<OrderItem> Items { get; private set; } = new List<OrderItem>();

    private Order() { }

    /// <summary>
    /// Factory — creates a fully-validated, Placed order.
    /// Callers must pass already-loaded aggregates to keep validation in the domain.
    /// </summary>
    public static Order Create(
        Parent parent,
        Student student,
        Canteen canteen,
        DateOnly fulfilmentDate,
        IReadOnlyList<(MenuItem item, int quantity)> lineItems,
        DateTimeOffset nowInCanteenTz)
    {
        // 1. Cut-off check
        var currentTime = TimeOnly.FromTimeSpan(nowInCanteenTz.TimeOfDay);
        if (!canteen.IsOrderAllowed(fulfilmentDate, currentTime))
            throw new OrderCutOffException(canteen.Name, canteen.CutOffTime, fulfilmentDate);

        // 2. Allergen check — before stock deduction
        foreach (var (item, _) in lineItems)
        {
            if (student.HasAllergenConflict(item.AllergenTags))
                throw new AllergenConflictException(student.Name, item.Name, item.AllergenTags);
        }

        // 3. Stock check — before wallet deduction
        foreach (var (item, qty) in lineItems)
        {
            if (item.DailyStock.HasValue && item.DailyStock < qty)
                throw new InsufficientStockException(item.Name, item.DailyStock.Value, qty);
        }

        // 4. Wallet check
        var total = lineItems.Sum(l => l.item.Price * l.quantity);
        if (parent.WalletBalance < total)
            throw new InsufficientWalletBalanceException(parent.WalletBalance, total);

        // --- All checks passed; apply side effects ---

        // Deduct stock
        foreach (var (item, qty) in lineItems)
            item.TryDeductStock(qty);

        // Debit wallet
        parent.DebitWallet(total);

        var order = new Order
        {
            Id = Guid.NewGuid(),
            ParentId = parent.Id,
            Parent = parent,
            StudentId = student.Id,
            Student = student,
            CanteenId = canteen.Id,
            Canteen = canteen,
            FulfilmentDate = fulfilmentDate,
            Status = OrderStatus.Placed,
            TotalAmount = total,
            CreatedAtUtc = nowInCanteenTz.UtcDateTime,
            Items = lineItems.Select(l => new OrderItem
            {
                Id = Guid.NewGuid(),
                MenuItemId = l.item.Id,
                MenuItem = l.item,
                Quantity = l.quantity,
                UnitPrice = l.item.Price
            }).ToList()
        };

        return order;
    }

    public void Cancel()
    {
        if (Status == OrderStatus.Fulfilled)
            throw new InvalidOperationException("Cannot cancel a fulfilled order.");
        Status = OrderStatus.Cancelled;
    }
}

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid MenuItemId { get; set; }
    public MenuItem MenuItem { get; set; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
}
