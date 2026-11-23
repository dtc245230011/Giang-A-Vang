namespace SupportTicket.Api.Security;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "SupportTicket.Api";
    public string Audience { get; set; } = "SupportTicket.Client";
    public int ExpiryMinutes { get; set; } = 120;
}
