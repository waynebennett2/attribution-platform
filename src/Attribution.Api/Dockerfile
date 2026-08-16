# FR-043: horizontal scale, no single point of failure in the visitor-facing allocation
# path — this image is what docker-compose.yml runs N copies of behind the load balancer.
# Build context is the repository root (docker-compose.yml's `context: .`), since the
# project references sibling projects outside src/Attribution.Api.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Attribution.Api/Attribution.Api.csproj src/Attribution.Api/
COPY src/Attribution.Application/Attribution.Application.csproj src/Attribution.Application/
COPY src/Attribution.Domain/Attribution.Domain.csproj src/Attribution.Domain/
COPY src/Attribution.Infrastructure/Attribution.Infrastructure.csproj src/Attribution.Infrastructure/
RUN dotnet restore src/Attribution.Api/Attribution.Api.csproj

COPY src/ src/
RUN dotnet publish src/Attribution.Api/Attribution.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
# curl isn't in the base runtime image; HEALTHCHECK below and the load balancer's own
# probe both need it.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app .

# Matches app.MapHealthChecks("/health") in Program.cs — docker-compose's healthcheck and
# the load balancer's passive-failure detection both key off this.
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Attribution.Api.dll"]
