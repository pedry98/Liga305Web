# syntax=docker/dockerfile:1.6
# Multi-stage build for Liga305 API.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj files first for better layer caching on dependency restores.
COPY Liga305.sln .
COPY src/Liga305.Api/Liga305.Api.csproj src/Liga305.Api/
COPY src/Liga305.Domain/Liga305.Domain.csproj src/Liga305.Domain/
COPY src/Liga305.Infrastructure/Liga305.Infrastructure.csproj src/Liga305.Infrastructure/
RUN dotnet restore src/Liga305.Api/Liga305.Api.csproj

# Copy the rest of the source and publish.
COPY src/Liga305.Api/ src/Liga305.Api/
COPY src/Liga305.Domain/ src/Liga305.Domain/
COPY src/Liga305.Infrastructure/ src/Liga305.Infrastructure/
RUN dotnet publish src/Liga305.Api/Liga305.Api.csproj \
    -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# .NET 9 base image already ships a non-root `app` user (UID/GID 1654) — use it.
COPY --from=build --chown=app:app /app ./
# wwwroot/avatars is a mounted volume in prod (persistent uploads).
RUN mkdir -p wwwroot/avatars && chown -R app:app wwwroot

USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

ENTRYPOINT ["dotnet", "Liga305.Api.dll"]
