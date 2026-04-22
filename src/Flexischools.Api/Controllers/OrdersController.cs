using Flexischools.Api.Application.Orders.Commands;
using Flexischools.Api.Application.Orders.Queries;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Flexischools.Api.Controllers;

[ApiController]
[Route("orders")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    public OrdersController(IMediator mediator) => _mediator = mediator;

    /// <summary>Create a new order. Supports Idempotency-Key header.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResult), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateOrder(
        [FromBody] CreateOrderRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var command = new CreateOrderCommand(
            request.ParentId,
            request.StudentId,
            request.CanteenId,
            request.FulfilmentDate,
            request.Lines,
            idempotencyKey);

        var result = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetOrder), new { id = result.OrderId }, result);
    }

    /// <summary>Get order status and details by ID.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOrder(Guid id, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetOrderQuery(id), ct);
        return Ok(result);
    }
}

/// <summary>
/// HTTP request body for POST /orders.
/// Kept separate from the MediatR command to decouple HTTP concerns from application logic.
/// </summary>
public record CreateOrderRequest(
    Guid ParentId,
    Guid StudentId,
    Guid CanteenId,
    DateOnly FulfilmentDate,
    IReadOnlyList<OrderLineDto> Lines
);
