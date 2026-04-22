namespace Flexischools.Api.Domain.Entities;

public class MenuItem
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public Guid CanteenId { get; private set; }
    public Canteen Canteen { get; private set; } = null!;

    /// <summary>
    /// Null means unlimited stock.
    /// </summary>
    public int? DailyStock { get; private set; }

    /// <summary>
    /// Allergen tags (e.g., "nuts", "dairy"). Stored as comma-separated string.
    /// </summary>
    public IReadOnlyList<string> AllergenTags { get; private set; } = new List<string>();

    // Optimistic concurrency token for stock updates
    public uint RowVersion { get; private set; }

    private MenuItem() { }

    public static MenuItem Create(string name, decimal price, Guid canteenId,
        int? dailyStock = null, IEnumerable<string>? allergenTags = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("MenuItem name is required.", nameof(name));
        if (price < 0) throw new ArgumentException("Price cannot be negative.", nameof(price));

        return new MenuItem
        {
            Id = Guid.NewGuid(),
            Name = name,
            Price = price,
            CanteenId = canteenId,
            DailyStock = dailyStock,
            AllergenTags = allergenTags?.Select(t => t.Trim().ToLowerInvariant()).ToList() ?? new List<string>()
        };
    }

    /// <summary>
    /// Attempts to deduct stock. Returns false if insufficient stock.
    /// </summary>
    public bool TryDeductStock(int quantity)
    {
        if (DailyStock is null) return true; // Unlimited

        if (DailyStock < quantity) return false;
        DailyStock -= quantity;
        return true;
    }

    /// <summary>
    /// Restores stock (used on order cancellation).
    /// </summary>
    public void RestoreStock(int quantity)
    {
        if (DailyStock is not null)
            DailyStock += quantity;
    }
}
