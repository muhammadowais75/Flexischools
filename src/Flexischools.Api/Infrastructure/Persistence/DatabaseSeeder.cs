using Flexischools.Api.Domain.Entities;

namespace Flexischools.Api.Infrastructure.Persistence;

/// <summary>
/// Seeds minimal reference data so the API is usable immediately after startup.
/// Idempotent — checks for existing data before inserting.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedAsync(AppDbContext db)
    {
        if (db.Parents.Any()) return; // Already seeded

        var parent = Parent.Create("Jane Smith", "jane.smith@example.com", 100.00m);
        var student = Student.Create("Tom Smith", parent.Id, new[] { "nuts" });

        var canteen = Canteen.Create(
            "Greenfield Primary Canteen",
            openDays: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            cutOffTime: new TimeSpan(9, 30, 0)); // 9:30 AM

        db.Parents.Add(parent);
        db.Students.Add(student);
        db.Canteens.Add(canteen);
        await db.SaveChangesAsync();

        var sandwich = MenuItem.Create("Vegemite Sandwich", 4.50m, canteen.Id, dailyStock: 20);
        var pie = MenuItem.Create("Meat Pie", 5.50m, canteen.Id, dailyStock: 15);
        var nutBar = MenuItem.Create("Nut Bar", 2.00m, canteen.Id, dailyStock: 30, allergenTags: new[] { "nuts" });
        var juice = MenuItem.Create("Apple Juice", 3.00m, canteen.Id); // Unlimited stock

        db.MenuItems.AddRange(sandwich, pie, nutBar, juice);
        await db.SaveChangesAsync();

        Console.WriteLine("=== SEED DATA ===");
        Console.WriteLine($"Parent Id  : {parent.Id}");
        Console.WriteLine($"Student Id : {student.Id}");
        Console.WriteLine($"Canteen Id : {canteen.Id}");
        Console.WriteLine($"MenuItem (sandwich) Id : {sandwich.Id}");
        Console.WriteLine($"MenuItem (pie)      Id : {pie.Id}");
        Console.WriteLine($"MenuItem (nut bar)  Id : {nutBar.Id}");
        Console.WriteLine($"MenuItem (juice)    Id : {juice.Id}");
        Console.WriteLine("=================");
    }
}
