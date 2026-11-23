FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SupportTicket.Worker/SupportTicket.Worker.csproj src/SupportTicket.Worker/
COPY src/SupportTicket.Infrastructure/SupportTicket.Infrastructure.csproj src/SupportTicket.Infrastructure/
COPY src/SupportTicket.Contracts/SupportTicket.Contracts.csproj src/SupportTicket.Contracts/
RUN dotnet restore src/SupportTicket.Worker/SupportTicket.Worker.csproj

COPY src/ src/
RUN dotnet publish src/SupportTicket.Worker/SupportTicket.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

ENV DOTNET_NOLOGO=true

COPY --from=build /app/publish .
USER app

ENTRYPOINT ["dotnet", "SupportTicket.Worker.dll"]
