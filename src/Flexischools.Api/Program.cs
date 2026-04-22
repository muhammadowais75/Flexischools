using Flexischools.Api.Application.Common;
using Flexischools.Api.Infrastructure.Idempotency;
using Flexischools.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// ── Logging ───────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// ── Persistence ───────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlite(
        builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=flexischools.db"));

// ── Application ───────────────────────────────────────────────────────────────
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

builder.Services.AddScoped<IdempotencyService>();

// TimeProvider is injected for testability (tests can substitute a fixed clock)
builder.Services.AddSingleton(TimeProvider.System);

// ── Web / API ─────────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Flexischools Ordering API",
        Version = "v1",
        Description = "Minimal ordering & payments slice for the Flexischools canteen platform."
    });
    c.EnableAnnotations();
});

// ── Health check (stretch goal: observability) ────────────────────────────────
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

var app = builder.Build();

// ── DB initialisation ─────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    await DatabaseSeeder.SeedAsync(db);
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Flexischools API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger at root
    });
}

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Exposed for WebApplicationFactory in integration tests
public partial class Program { }
