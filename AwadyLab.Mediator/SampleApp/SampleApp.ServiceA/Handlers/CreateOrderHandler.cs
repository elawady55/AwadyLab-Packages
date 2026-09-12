using AwadyLab.Mediator.Abstraction;
using SampleApp.ServiceA.Models;

namespace SampleApp.ServiceA.Handlers;

public sealed class CreateOrderHandler(ILogger<CreateOrderHandler> logger)
    : IRequestHandler<CreateOrderCommand, OrderResult>
{
    // Flat rate per unit, just so /orders has a Total worth looking at — there's no real pricing here.
    private const decimal UnitPrice = 9.99m;

    public Task<OrderResult> Execute(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var orderId = Guid.NewGuid();
        var total = request.Quantity * UnitPrice;

        logger.LogInformation("Order {OrderId} created for {CustomerName}: {Quantity} x {Sku}.",
            orderId, request.CustomerName, request.Quantity, request.Sku);

        return Task.FromResult(new OrderResult(orderId, request.CustomerName, total));
    }
}
