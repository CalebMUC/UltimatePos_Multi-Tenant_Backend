# The API image. Build from the repository root:
#   docker build -f src/UltimatePos.Api/Dockerfile -t ultimatepos-api .
# Runs the API on port 8080. With the argument --migrate it updates the database and exits instead (the compose `migrate` service).

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first, so the package restore is cached until a project file changes.
COPY src/UltimatePos.Domain/UltimatePos.Domain.csproj src/UltimatePos.Domain/
COPY src/UltimatePos.Application/UltimatePos.Application.csproj src/UltimatePos.Application/
COPY src/UltimatePos.Infrastructure/UltimatePos.Infrastructure.csproj src/UltimatePos.Infrastructure/
COPY src/UltimatePos.Api/UltimatePos.Api.csproj src/UltimatePos.Api/
RUN dotnet restore src/UltimatePos.Api/UltimatePos.Api.csproj

COPY src/ src/
RUN dotnet publish src/UltimatePos.Api/UltimatePos.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# The non-root user the .NET images provide.
USER app
ENTRYPOINT ["dotnet", "UltimatePos.Api.dll"]
