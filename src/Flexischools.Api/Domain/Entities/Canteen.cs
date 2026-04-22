namespace Flexischools.Api.Domain.Entities;

/// <summary>
/// Represents a school canteen with per-day opening and cut-off configuration.
/// </summary>
public class Canteen
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>
    /// Days this canteen is open (0=Sunday … 6=Saturday).
    /// Stored as comma-separated ints in the DB.
    /// </summary>
    public IReadOnlyList<DayOfWeek> OpenDays { get; private set; } = new List<DayOfWeek>();

    /// <summary>
    /// Order cut-off time (same for all open days in this simplified model).
    /// </summary>
    public TimeSpan CutOffTime { get; private set; }

    public ICollection<MenuItem> MenuItems { get; private set; } = new List<MenuItem>();

    private Canteen() { }

    public static Canteen Create(string name, IEnumerable<DayOfWeek> openDays, TimeSpan cutOffTime)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Canteen name is required.", nameof(name));

        return new Canteen
        {
            Id = Guid.NewGuid(),
            Name = name,
            OpenDays = openDays.ToList(),
            CutOffTime = cutOffTime
        };
    }

    /// <summary>
    /// Returns true if an order can still be placed for the given fulfilment date, given the current time.
    /// Both parameters should be in the canteen's local time zone (caller is responsible for conversion).
    /// </summary>
    public bool IsOrderAllowed(DateOnly fulfilmentDate, TimeOnly currentTime)
    {
        if (!OpenDays.Contains(fulfilmentDate.DayOfWeek))
            return false;

        return currentTime < TimeOnly.FromTimeSpan(CutOffTime);
    }
}
