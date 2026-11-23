namespace SupportTicket.Contracts;

/// <summary>Vai trò người dùng trong hệ thống.</summary>
public enum UserRole
{
    Customer = 0,
    Agent = 1,
    Admin = 2
}

/// <summary>Danh mục yêu cầu hỗ trợ. Dùng để chọn nhóm xử lý.</summary>
public enum TicketCategory
{
    Account = 0,
    Payment = 1,
    Delivery = 2,
    Other = 3
}

/// <summary>Mức ưu tiên của yêu cầu.</summary>
public enum TicketPriority
{
    Low = 0,
    Medium = 1,
    High = 2
}

/// <summary>Trạng thái vòng đời của yêu cầu.</summary>
public enum TicketStatus
{
    New = 0,
    Assigned = 1,
    InProgress = 2,
    Resolved = 3
}
