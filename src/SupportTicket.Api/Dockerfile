FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/SupportTicket.Api/SupportTicket.Api.csproj src/SupportTicket.Api/
COPY src/SupportTicket.Infrastructure/SupportTicket.Infrastructure.csproj src/SupportTicket.Infrastructure/
COPY src/SupportTicket.Contracts/SupportTicket.Contracts.csproj src/SupportTicket.Contracts/
RUN dotnet restore src/SupportTicket.Api/SupportTicket.Api.csproj

COPY src/ src/
RUN dotnet publish src/SupportTicket.Api/SupportTicket.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=true

COPY --from=build /app/publish .

EXPOSE 8080
USER app

HEALTHCHECK --interval=15s --timeout=5s --start-period=90s --retries=10 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "SupportTicket.Api.dll"]
