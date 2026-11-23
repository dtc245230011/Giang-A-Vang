using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace SupportTicket.Infrastructure.Messaging;

/// <summary>Quản lý một kết nối RabbitMQ dùng chung, tự kết nối lại khi mất mạng.</summary>
public sealed class RabbitMqConnection(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RabbitMqOptions _options = options.Value;
    private IConnection? _connection;

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken)
    {
        var current = _connection;
        if (current is { IsOpen: true })
        {
            return current;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true })
            {
                return _connection;
            }

            if (_connection is not null)
            {
                await SafeDisposeAsync(_connection);
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                VirtualHost = _options.VirtualHost,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                ClientProvidedName = "support-ticket-system"
            };

            logger.LogInformation("Đang kết nối RabbitMQ tại {Host}:{Port}...", _options.Host, _options.Port);
            _connection = await factory.CreateConnectionAsync(cancellationToken);
            logger.LogInformation("Đã kết nối RabbitMQ.");
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await SafeDisposeAsync(_connection);
            _connection = null;
        }

        _gate.Dispose();
    }

    private static async Task SafeDisposeAsync(IConnection connection)
    {
        try
        {
            await connection.CloseAsync();
        }
        catch
        {
            // Bỏ qua lỗi đóng kết nối.
        }

        await connection.DisposeAsync();
    }
}
