using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupportTicket.Contracts;

/// <summary>Cấu hình JSON thống nhất cho Outbox, message RabbitMQ và API.</summary>
public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
