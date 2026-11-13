using SneakerStore.Messaging;

namespace SneakerStore.Worker;

public interface IReceiptSender
{
    Task SendAsync(OrderPlaced order);
}

/// <summary>
/// Stands in for an email provider. It logs instead of sending, and it refuses any address under
/// .invalid, a top-level domain reserved for tests (RFC 2606). That gives the demo an order that
/// can never succeed, however many times it is delivered.
/// </summary>
public sealed class SimulatedReceiptSender(ILogger<SimulatedReceiptSender> logger) : IReceiptSender
{
    public async Task SendAsync(OrderPlaced order)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(300));

        if (order.Email.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The mail provider refused {order.Email}.");
        }

        logger.LogInformation("Receipt for order {OrderId} sent to {Email}.", order.OrderId, order.Email);
    }
}
