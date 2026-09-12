FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy the project file ALONE and restore before copying any source. Docker caches each
# layer, so as long as the dependencies have not changed this restore is reused - and a
# one-line code change does not re-download every NuGet package.
COPY BackendApplication/BackendApplication.csproj BackendApplication/
RUN dotnet restore BackendApplication/BackendApplication.csproj

COPY BackendApplication/ BackendApplication/

RUN dotnet publish BackendApplication/BackendApplication.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish


# A second stage on the runtime image. The SDK, the source and the NuGet cache all stay
# behind in the build stage - the shipped image carries only the published output.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Back to the unprivileged user the base image provides. A container running as root is a
# container one bug away from being a host problem.
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Docker \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=true \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=true

EXPOSE 8080

COPY --from=build /app/publish .

HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=5 \
    CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health/ready || exit 1"]

ENTRYPOINT ["dotnet", "BackendApplication.dll"]
