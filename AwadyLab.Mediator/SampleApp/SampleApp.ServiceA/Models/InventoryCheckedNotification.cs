using AwadyLab.Mediator.Abstraction;

namespace SampleApp.ServiceA.Models;

/// <summary>Direct-delivery demo: published and handled entirely in-process, never leaves Service A.</summary>
public sealed record InventoryCheckedNotification(string Sku, int QuantityOnHand) : INotification;
