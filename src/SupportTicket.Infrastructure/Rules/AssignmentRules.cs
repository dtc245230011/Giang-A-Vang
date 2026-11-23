using SupportTicket.Contracts;

namespace SupportTicket.Infrastructure.Rules;

/// <summary>Quy tắc chọn nhóm xử lý theo danh mục yêu cầu.</summary>
public static class AssignmentRules
{
    public const string Tech = "TECH";
    public const string Accounting = "ACCT";
    public const string CustomerCare = "CSKH";
    public const string General = "GENERAL";

    public static string ResolveTeamCode(TicketCategory category) => category switch
    {
        TicketCategory.Account => Tech,
        TicketCategory.Payment => Accounting,
        TicketCategory.Delivery => CustomerCare,
        _ => General
    };
}
