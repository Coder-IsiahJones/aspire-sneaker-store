using RabbitMQ.Client;

namespace SneakerStore.Messaging;

/// <summary>
/// Every exchange, queue and routing key in one place. The API and the worker both call
/// <see cref="DeclareAsync"/> before they use the broker, so either one can start first.
/// </summary>
public static class Topology
{
    public const string Exchange = "stockroom.orders";
    public const string DeadLetterExchange = "stockroom.dead";

    public static class Keys
    {
        public const string OrderPlaced = "order.placed";
        public const string OrderConfirmed = "order.confirmed";
        public const string OrderPacked = "order.packed";

        /// <summary>A binding pattern, not a key to publish with. # stands for zero or more words.</summary>
        public const string AnyOrderEvent = "order.#";
    }

    public static class Queues
    {
        public const string Receipts = "receipts";
        public const string Warehouse = "warehouse";
        public const string DeadLetters = "dead-letters";

        /// <summary>The queues that workers consume. Each gets its own copy of every placed order.</summary>
        public static readonly string[] Work = [Receipts, Warehouse];
    }

    /// <summary>
    /// How many times a work queue redelivers a message that keeps failing. With 3, a message is
    /// delivered at most four times: once, then three more. After that the broker moves it to
    /// <see cref="Queues.DeadLetters"/>. A reject or a crashed consumer counts as a failure.
    /// A nack with requeue does not, so it would loop forever.
    /// </summary>
    public const int DeliveryLimit = 3;

    /// <summary>
    /// Declaring is idempotent: if the exchange or queue already exists with the same settings,
    /// nothing happens. Different settings close the channel with an error.
    /// </summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        await channel.ExchangeDeclareAsync(
            Exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.ExchangeDeclareAsync(
            DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            Queues.DeadLetters,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" },
            cancellationToken: cancellationToken);
        await channel.QueueBindAsync(
            Queues.DeadLetters, DeadLetterExchange, routingKey: "", cancellationToken: cancellationToken);

        foreach (var queue in Queues.Work)
        {
            await channel.QueueDeclareAsync(
                queue,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    // Delivery limits are a quorum queue feature.
                    ["x-queue-type"] = "quorum",
                    ["x-delivery-limit"] = DeliveryLimit,
                    ["x-dead-letter-exchange"] = DeadLetterExchange,
                },
                cancellationToken: cancellationToken);
            await channel.QueueBindAsync(queue, Exchange, Keys.OrderPlaced, cancellationToken: cancellationToken);
        }
    }
}
