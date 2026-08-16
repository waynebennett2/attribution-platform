# FR-043: "each loop is independently registered so a slow/failing one never blocks the
# others, and the whole host can scale independently from the request/response Api"
# (Program.cs) — this image is what docker-compose.yml runs N copies of. Build context is
# the repository root, since the project references sibling projects outside
# src/Attribution.Workers.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/Attribution.Workers/Attribution.Workers.csproj src/Attribution.Workers/
COPY src/Attribution.Application/Attribution.Application.csproj src/Attribution.Application/
COPY src/Attribution.Domain/Attribution.Domain.csproj src/Attribution.Domain/
COPY src/Attribution.Infrastructure/Attribution.Infrastructure.csproj src/Attribution.Infrastructure/
RUN dotnet restore src/Attribution.Workers/Attribution.Workers.csproj

COPY src/ src/
RUN dotnet publish src/Attribution.Workers/Attribution.Workers.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENTRYPOINT ["dotnet", "Attribution.Workers.dll"]
