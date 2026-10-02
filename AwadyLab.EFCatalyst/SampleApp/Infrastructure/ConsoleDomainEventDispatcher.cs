using AwadyLab.EFCatalyst.Abstractions;

namespace SampleApp.Infrastructure;

/// <summary>Receives the domain events collected from aggregates, after the save succeeded.</summary>
public sealed class ConsoleDomainEventDispatcher : IDomainEventDispatcher
{
    public ValueTask DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken)
    {
        foreach (var domainEvent in domainEvents)
        {
            Output.Line($"dispatched {domainEvent}");
        }

        return ValueTask.CompletedTask;
    }
}
