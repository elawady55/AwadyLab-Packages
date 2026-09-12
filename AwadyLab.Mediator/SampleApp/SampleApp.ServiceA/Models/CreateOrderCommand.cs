using AwadyLab.Mediator.Abstraction;

namespace SampleApp.ServiceA.Models;

/// <summary>Request/response demo (<c>IRequestMediator</c>), bonus scenario alongside the notification pillars.</summary>
public sealed record CreateOrderCommand(string CustomerName, string Sku, int Quantity) : IRequest<OrderResult>;

public sealed record OrderResult(Guid OrderId, string CustomerName, decimal Total) : IResponse;
