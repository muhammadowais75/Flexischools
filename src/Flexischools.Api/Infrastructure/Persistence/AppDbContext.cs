using Flexischools.Api.Domain.Entities;
using Flexischools.Api.Infrastructure.Idempotency;
using Microsoft.EntityFrameworkCore;

namespace Flexischools.Api.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Canteen> Canteens => Set<Canteen>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
