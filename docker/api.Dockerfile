# Build context: repository root
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props Mdr.sln ./
COPY src/Mdr.Domain/Mdr.Domain.csproj src/Mdr.Domain/
COPY src/Mdr.Application/Mdr.Application.csproj src/Mdr.Application/
COPY src/Mdr.Infrastructure/Mdr.Infrastructure.csproj src/Mdr.Infrastructure/
COPY src/Mdr.Api/Mdr.Api.csproj src/Mdr.Api/
RUN dotnet restore src/Mdr.Api/Mdr.Api.csproj
COPY src/Mdr.Domain/ src/Mdr.Domain/
COPY src/Mdr.Application/ src/Mdr.Application/
COPY src/Mdr.Infrastructure/ src/Mdr.Infrastructure/
COPY src/Mdr.Api/ src/Mdr.Api/
RUN dotnet publish src/Mdr.Api/Mdr.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data/uploads && chown -R app:app /app/data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Storage__RootPath=/app/data/uploads
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=5 \
  CMD curl --fail --silent http://localhost:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "Mdr.Api.dll"]
