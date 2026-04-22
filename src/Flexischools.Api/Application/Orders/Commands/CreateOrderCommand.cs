using Flexischools.Api.Application.Common;
using Flexischools.Api.Domain.Entities;
using Flexischools.Api.Domain.Exceptions;
using Flexischools.Api.Infrastructure.Idempotency;
using Flexischools.Api.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Flexischools.Api.Application.Orders.Commands;

// ── Request ──────────────────────────────────────────────────────────────────

public record CreateOrderCommand(
    Guid ParentId,
    Guid StudentId,
    Guid CanteenId,
    DateOnly FulfilmentDate,
    IReadOnlyList<OrderLineDto> Lines,
    string? IdempotencyKey
) : IRequest<CreateOrderResult>;

public record OrderLineDto(Guid MenuItemId, int Quantity);

// ── Result ────────────────────────────────────────────────────────────────────

public record CreateOrderResult(Guid OrderId, decimal Total, string Status);

// ── Handler ───────────────────────────────────────────────────────────────────

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly AppDbContext _db;
    private readonly IdempotencyService _idempotency;
    private readonly ILogger<CreateOrderCommandHandler> _logger;
    private readonly TimeProvider _timeProvider;

    public CreateOrderCommandHandler(
        AppDbContext db,
        IdempotencyService idempotency,
        ILogger<CreateOrderCommandHandler> logger,
        TimeProvider timeProvider)
    {
        _db = db;
        _idempotency = idempotency;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken ct)
    {
        using var logScope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["ParentId"] = request.ParentId,
            ["StudentId"] = request.StudentId,
            ["CanteenId"] = request.CanteenId,
            ["FulfilmentDate"] = request.FulfilmentDate,
            ["IdempotencyKey"] = request.IdempotencyKey ?? "(none)"
        });

        _logger.LogInformation("Handling CreateOrder for parent {ParentId}, student {StudentId}",
            request.ParentId, request.StudentId);

        // ── Idempotency short-circuit ─────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existing = await _idempotency.GetAsync(request.IdempotencyKey, ct);
            if (existing is not null)
            {
                _logger.LogInformation("Returning cached idempotent response for key {Key}", request.IdempotencyKey);
                return JsonSerializer.Deserialize<CreateOrderResult>(existing.ResponseBody)!;
            }
        }

        // ── Load aggregates (with row-level locks via transaction) ─────────────
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            var parent = await _db.Parents.FirstOrDefaultAsync(p => p.Id == request.ParentId, ct)
                ?? throw new NotFoundException(nameof(Parent), request.ParentId);

            var student = await _db.Students.FirstOrDefaultAsync(s =>
                s.Id == request.StudentId && s.ParentId == request.ParentId, ct)
                ?? throw new NotFoundException(nameof(Student), request.StudentId);

            var canteen = await _db.Canteens.FirstOrDefaultAsync(c => c.Id == request.CanteenId, ct)
                ?? throw new NotFoundException(nameof(Canteen), request.CanteenId);

            var menuItemIds = request.Lines.Select(l => l.MenuItemId).ToList();
            var menuItems = await _db.MenuItems
                .Where(m => menuItemIds.Contains(m.Id) && m.CanteenId == request.CanteenId)
                .ToListAsync(ct);

            // Validate all requested items belong to this canteen
            var missingIds = menuItemIds.Except(menuItems.Select(m => m.Id)).ToList();
            if (missingIds.Any())
                throw new NotFoundException(nameof(MenuItem), string.Join(", ", missingIds));

            var lineItems = request.Lines
                .Select(l => (menuItems.First(m => m.Id == l.MenuItemId), l.Quantity))
                .ToList();

            _logger.LogInformation("Order lines: {Lines}, estimated total: {Total}",
                string.Join("; ", lineItems.Select(l => $"{l.Item1.Name} x{l.Quantity}")),
                lineItems.Sum(l => l.Item1.Price * l.Quantity));

            // ── Domain factory — all business rules enforced inside ────────────
            // Convert UTC now → Sydney/AEST for cut-off comparison
            var canteenTz = TimeZoneInfo.FindSystemTimeZoneById("Australia/Sydney");
            var nowInCanteenTz = TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), canteenTz);

            var order = Order.Create(parent, student, canteen, request.FulfilmentDate, lineItems, nowInCanteenTz);
            _db.Orders.Add(order);
            await _db.SaveChangesAsync(ct);

            // ── Persist idempotency record in same transaction ─────────────────
            var result = new CreateOrderResult(order.Id, order.TotalAmount, order.Status.ToString());
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
                await _idempotency.SaveAsync(request.IdempotencyKey, 201, JsonSerializer.Serialize(result), ct);

            await tx.CommitAsync(ct);

            _logger.LogInformation("Order {OrderId} created successfully. Total: {Total}",
                order.Id, order.TotalAmount);

            return result;
        }
        catch (Exception ex) when (ex is not DomainException)
        {
            await tx.RollbackAsync(ct);
            _logger.LogError(ex, "Unexpected error creating order");
            throw;
        }
        catch (DomainException ex)
        {
            await tx.RollbackAsync(ct);
            _logger.LogWarning("Order rejected: {Reason}", ex.Message);
            throw;
        }
    }
}
