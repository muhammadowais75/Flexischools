using Flexischools.Api.Domain.Entities;
using Flexischools.Api.Domain.Exceptions;
using Flexischools.Api.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Flexischools.Api.Application.Orders.Queries;

// ── Request ───────────────────────────────────────────────────────────────────

public record GetOrderQuery(Guid OrderId) : IRequest<OrderDetailDto>;

// ── DTO ───────────────────────────────────────────────────────────────────────

public record OrderDetailDto(
    Guid Id,
    Guid ParentId,
    string ParentName,
    Guid StudentId,
    string StudentName,
    Guid CanteenId,
    string CanteenName,
    string FulfilmentDate,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    IReadOnlyList<OrderItemDto> Items
);

public record OrderItemDto(
    Guid MenuItemId,
    string MenuItemName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal
);

// ── Handler ───────────────────────────────────────────────────────────────────

public class GetOrderQueryHandler : IRequestHandler<GetOrderQuery, OrderDetailDto>
{
    private readonly AppDbContext _db;

    public GetOrderQueryHandler(AppDbContext db) => _db = db;

    public async Task<OrderDetailDto> Handle(GetOrderQuery request, CancellationToken ct)
    {
        var order = await _db.Orders
            .AsNoTracking()
            .Include(o => o.Parent)
            .Include(o => o.Student)
            .Include(o => o.Canteen)
            .Include(o => o.Items).ThenInclude(i => i.MenuItem)
            .FirstOrDefaultAsync(o => o.Id == request.OrderId, ct)
            ?? throw new NotFoundException(nameof(Order), request.OrderId);

        return new OrderDetailDto(
            Id: order.Id,
            ParentId: order.ParentId,
            ParentName: order.Parent.Name,
            StudentId: order.StudentId,
            StudentName: order.Student.Name,
            CanteenId: order.CanteenId,
            CanteenName: order.Canteen.Name,
            FulfilmentDate: order.FulfilmentDate.ToString("yyyy-MM-dd"),
            Status: order.Status.ToString(),
            TotalAmount: order.TotalAmount,
            CreatedAtUtc: order.CreatedAtUtc,
            Items: order.Items.Select(i => new OrderItemDto(
                MenuItemId: i.MenuItemId,
                MenuItemName: i.MenuItem.Name,
                Quantity: i.Quantity,
                UnitPrice: i.UnitPrice,
                LineTotal: i.Quantity * i.UnitPrice
            )).ToList()
        );
    }
}
