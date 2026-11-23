using System.Security.Cryptography;

namespace SupportTicket.Infrastructure.Rules;

public static class TicketNumberGenerator
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>Sinh mã dễ đọc dạng TK-261123-7K2M. Tính duy nhất được bảo đảm bởi unique index và retry.</summary>
    public static string Create(DateTime utcNow)
    {
        Span<char> suffix = stackalloc char[4];
        for (var i = 0; i < suffix.Length; i++)
        {
            suffix[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return $"TK-{utcNow:yyMMdd}-{new string(suffix)}";
    }
}
