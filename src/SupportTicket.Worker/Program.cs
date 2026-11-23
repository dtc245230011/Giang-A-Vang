using SupportTicket.Infrastructure;
using SupportTicket.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSupportTicketInfrastructure(builder.Configuration);
builder.Services.AddHostedService<AssignmentWorkerService>();

var host = builder.Build();
host.Run();
