namespace Flexischools.Api.Domain.Entities;

public class Student
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid ParentId { get; private set; }
    public Parent Parent { get; private set; } = null!;

    /// <summary>
    /// Optional allergen tags recorded for this student (e.g., "nuts", "dairy").
    /// Stored as a comma-separated string in the DB column.
    /// </summary>
    public IReadOnlyList<string> Allergens { get; private set; } = new List<string>();

    private Student() { }

    public static Student Create(string name, Guid parentId, IEnumerable<string>? allergens = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Student name is required.", nameof(name));

        return new Student
        {
            Id = Guid.NewGuid(),
            Name = name,
            ParentId = parentId,
            Allergens = allergens?.Select(a => a.Trim().ToLowerInvariant()).ToList() ?? new List<string>()
        };
    }

    /// <summary>
    /// Returns true if any of the provided allergen tags conflict with the student's recorded allergens.
    /// </summary>
    public bool HasAllergenConflict(IEnumerable<string> menuItemAllergens)
        => Allergens.Any(sa => menuItemAllergens.Contains(sa, StringComparer.OrdinalIgnoreCase));
}
