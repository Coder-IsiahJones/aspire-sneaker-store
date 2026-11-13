using RabbitMQ.Client;
using SneakerStore.Messaging;

namespace SneakerStore.Worker;

/// <summary>Emails a receipt for each placed order.</summary>
public sealed class ReceiptWorker(
    IConnection connection,
    IReceiptSender receipts,
    OrderProgress orders,
    EventPublisher events,
    ILogger<ReceiptWorker> logger)
    : QueueWorker(connection, logger)
{
    protected override string Queue => Topology.Queues.Receipts;

    public override async Task HandleAsync(OrderPlaced order)
    {
        // If the provider refuses, this throws and the order is delivered again.
        await receipts.SendAsync(order);

        // Delivery is at-least-once. If this worker dies after sending and before acknowledging,
        // the order comes back and the shopper gets a second email. The timestamp below can't be
        // written twice, but nothing here can unsend an email.
        var at = DateTime.UtcNow;
        await orders.ReceiptSentAsync(order.OrderId, at);
        await events.PublishAsync(
            Topology.Keys.OrderConfirmed, new OrderConfirmed(order.OrderId, order.AccountId, at));
    }
}
