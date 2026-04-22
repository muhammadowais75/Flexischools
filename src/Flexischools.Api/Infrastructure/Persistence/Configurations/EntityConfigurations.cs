using Flexischools.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flexischools.Api.Infrastructure.Persistence.Configurations;

public class ParentConfiguration : IEntityTypeConfiguration<Parent>
{
    public void Configure(EntityTypeBuilder<Parent> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Email).IsRequired().HasMaxLength(300);
        builder.Property(p => p.WalletBalance).HasColumnType("decimal(18,2)");
        // Optimistic concurrency
        builder.Property(p => p.RowVersion).IsRowVersion();
    }
}

public class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        // Store allergens as a comma-separated string
        builder.Property(s => s.Allergens)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? new List<string>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
            .HasColumnName("Allergens");
        builder.HasOne(s => s.Parent)
            .WithMany(p => p.Students)
            .HasForeignKey(s => s.ParentId);
    }
}

public class CanteenConfiguration : IEntityTypeConfiguration<Canteen>
{
    public void Configure(EntityTypeBuilder<Canteen> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        // Store open days as comma-separated ints
        builder.Property(c => c.OpenDays)
            .HasConversion(
                v => string.Join(',', v.Select(d => (int)d)),
                v => v.Length == 0
                    ? new List<DayOfWeek>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(x => (DayOfWeek)int.Parse(x)).ToList())
            .HasColumnName("OpenDays");
        builder.Property(c => c.CutOffTime).HasColumnName("CutOffTime");
    }
}

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Price).HasColumnType("decimal(18,2)");
        builder.Property(m => m.AllergenTags)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Length == 0
                    ? new List<string>()
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
            .HasColumnName("AllergenTags");
        builder.Property(m => m.RowVersion).IsRowVersion();
        builder.HasOne(m => m.Canteen)
            .WithMany(c => c.MenuItems)
            .HasForeignKey(m => m.CanteenId);
    }
}

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(o => o.Id);
        builder.Property(o => o.TotalAmount).HasColumnType("decimal(18,2)");
        builder.Property(o => o.Status).HasConversion<string>();
        builder.HasOne(o => o.Parent).WithMany().HasForeignKey(o => o.ParentId);
        builder.HasOne(o => o.Student).WithMany().HasForeignKey(o => o.StudentId);
        builder.HasOne(o => o.Canteen).WithMany().HasForeignKey(o => o.CanteenId);
        builder.HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId);
    }
}

public class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.UnitPrice).HasColumnType("decimal(18,2)");
        builder.HasOne(i => i.MenuItem).WithMany().HasForeignKey(i => i.MenuItemId);
    }
}
