using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SupportTicket.Contracts;

namespace SupportTicket.Infrastructure.Messaging;

/// <summary>
/// Gửi message bền vững với publisher confirm. Message không định tuyến được sẽ bị coi là lỗi.
/// </summary>
public sealed class RabbitMqPublisher(
    RabbitMqConnection connection, IOptions<RabbitMqOptions> options, ILogger<RabbitMqPublisher> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RabbitMqOptions _options = options.Value;
    private IChannel? _channel;

    public async Task PublishAsync(
        Guid messageId, string routingKey, string eventType, string payload, CancellationToken cancellationToken)
    {
        await PublishCoreAsync(
            MessagingTopology.Exchange, routingKey, messageId, eventType, payload,
            retryCount: null, cancellationToken);
    }

    /// <summary>Gửi message tới exchange/queue chỉ định, dùng cho retry queue và DLQ.</summary>
    public async Task PublishRawAsync(
        string exchange, string routingKey, Guid messageId, string eventType, string payload,
        int retryCount, CancellationToken cancellationToken)
    {
        await PublishCoreAsync(exchange, routingKey, messageId, eventType, payload, retryCount, cancellationToken);
    }

    private async Task PublishCoreAsync(
        string exchange, string routingKey, Guid messageId, string eventType, string payload,
        int? retryCount, CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            throw new InvalidOperationException("RabbitMQ đang bị tắt trong cấu hình.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await EnsureChannelAsync(cancellationToken);

            var properties = new BasicProperties
            {
                Persistent = true,
                MessageId = messageId.ToString(),
                Type = eventType,
                ContentType = "application/json",
                ContentEncoding = "utf-8",
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            };

            if (retryCount is { } attempt)
            {
                properties.Headers = new Dictionary<string, object?> { ["x-retry-count"] = attempt };
            }

            var body = Encoding.UTF8.GetBytes(payload);

            // Khi bật PublisherConfirmationTracking, BasicPublishAsync chỉ hoàn tất sau khi broker
            // xác nhận message; đồng thời ném PublishException nếu bị nack hoặc basic.return.
            await channel.BasicPublishAsync(
                exchange, routingKey, mandatory: true, basicProperties: properties,
                body: body, cancellationToken: cancellationToken);
        }
        catch
        {
            await ResetChannelAsync();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> EnsureChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        var connection = await GetConnectionAsync(cancellationToken);
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);

        _channel.BasicReturnAsync += (_, args) =>
        {
            logger.LogError(
                "Message {MessageId} bị trả lại: {ReplyCode} {ReplyText}",
                args.BasicProperties?.MessageId, args.ReplyCode, args.ReplyText);
            return Task.CompletedTask;
        };

        await Topology.DeclareAsync(_channel, cancellationToken);
        return _channel;
    }

    private Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
        => connection.GetConnectionAsync(cancellationToken);

    private async Task ResetChannelAsync()
    {
        if (_channel is null)
        {
            return;
        }

        try
        {
            if (_channel.IsOpen)
            {
                await _channel.CloseAsync();
            }
        }
        catch
        {
            // Bỏ qua.
        }

        await _channel.DisposeAsync();
        _channel = null;
    }

    public async ValueTask DisposeAsync()
    {
        await ResetChannelAsync();
        _gate.Dispose();
    }
}
