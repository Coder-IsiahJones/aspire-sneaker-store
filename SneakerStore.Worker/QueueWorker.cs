using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using SneakerStore.Messaging;

namespace SneakerStore.Worker;

/// <summary>
/// Takes placed orders from one queue, one at a time. An order is acknowledged only after
/// <see cref="HandleAsync"/> finishes, so if this process dies halfway through, the broker hands the
/// order to another worker.
/// </summary>
public abstract class QueueWorker(IConnection connection, ILogger logger) : BackgroundService
{
    private Task _inHand = Task.CompletedTask;

    protected abstract string Queue { get; }

    /// <summary>Does the work for one order. Throwing means "this failed, deliver it again".</summary>
    public abstract Task HandleAsync(OrderPlaced order);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await Topology.DeclareAsync(channel, stoppingToken);

        // Prefetch 1: the broker won't send this worker a second order until it has settled the first.
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => _inHand = ProcessAsync(channel, delivery);
        var consumerTag = await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // The host is shutting down.
        }

        // Take no new orders, and finish the one in hand before the channel closes.
        await channel.BasicCancelAsync(consumerTag);
        await _inHand;
    }

    private async Task ProcessAsync(IChannel channel, BasicDeliverEventArgs delivery)
    {
        try
        {
            var order = MessageJson.Deserialize<OrderPlaced>(delivery.Body);
            await HandleAsync(order);
            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false);
        }
        catch (Exception e)
        {
            logger.LogWarning(
                e, "A delivery from {Queue} failed (redelivered: {Redelivered}).", Queue, delivery.Redelivered);

            // Reject, not nack. On RabbitMQ 4.3 only a reject counts toward the queue's delivery limit.
            // A nack with requeue would bring a bad order back forever.
            await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: true);
        }
    }
}
