using RabbitMQ.Client;

namespace SneakerStore.Messaging;

/// <summary>
/// Publishes order events to <see cref="Topology.Exchange"/>. It keeps one channel open for the life
/// of the app, and lets one caller publish at a time, because a channel must not be used by
/// concurrent publishers. The connection is asked for on first use, so an app can start, and fail
/// one publish at a time, while the broker is unreachable.
/// </summary>
public sealed class EventPublisher(Func<IConnection> connect) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IChannel? _channel;

    /// <summary>
    /// Completes once the broker has confirmed that it has the message, and throws if it refuses it.
    /// A confirm doesn't mean a queue got a copy. With <paramref name="mandatory"/> set, this also
    /// throws when no queue is bound for the routing key.
    /// </summary>
    public async Task PublishAsync<T>(
        string routingKey,
        T message,
        bool mandatory = false,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await OpenChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                // Written to disk, so the message survives a broker restart.
                DeliveryMode = DeliveryModes.Persistent,
                MessageId = Guid.NewGuid().ToString("N"),
                Type = typeof(T).Name,
            };

            await channel.BasicPublishAsync(
                Topology.Exchange, routingKey, mandatory, properties, MessageJson.Serialize(message), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> OpenChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        // A channel closes for good after a protocol error, so replace it.
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        // With confirmations tracked, awaiting BasicPublishAsync waits for the broker's answer.
        _channel = await connect().CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        await Topology.DeclareAsync(_channel, cancellationToken);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }

        _gate.Dispose();
    }
}
