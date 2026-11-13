using RabbitMQ.Client;
using SneakerStore.Messaging;

namespace SneakerStore.Worker;

/// <summary>Packs each placed order. This is the slow job, so it is the queue that backs up in a rush.</summary>
public sealed class WarehouseWorker(
    IConnection connection,
    IConfiguration configuration,
    OrderProgress orders,
    EventPublisher events,
    ILogger<WarehouseWorker> logger)
    : QueueWorker(connection, logger)
{
    private readonly TimeSpan _packingTime =
        TimeSpan.FromSeconds(configuration.GetValue("Warehouse:PackingSeconds", 2.0));

    protected override string Queue => Topology.Queues.Warehouse;

    public override async Task HandleAsync(OrderPlaced order)
    {
        // Nothing is really packed. The wait stands in for the work.
        await Task.Delay(_packingTime);

        var at = DateTime.UtcNow;
        await orders.PackedAsync(order.OrderId, at);
        await events.PublishAsync(
            Topology.Keys.OrderPacked, new OrderPacked(order.OrderId, order.AccountId, at));

        logger.LogInformation("Packed order {OrderId}: {Pairs} pair(s).", order.OrderId, order.Pairs);
    }
}
