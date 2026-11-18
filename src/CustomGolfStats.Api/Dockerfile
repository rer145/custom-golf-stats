# syntax=docker/dockerfile:1
# Build from the repository root:
#   docker build -f src/CustomGolfStats.Api/Dockerfile -t custom-golf-stats-api .                     # the API (default target)
#   docker build -f src/CustomGolfStats.Api/Dockerfile --target migrator -t custom-golf-stats-migrator .  # the migrations bundle
# The SDK stages run on the build machine's own platform and cross-compile for TARGETARCH, so a multi-arch build never
# runs the SDK under emulation.

ARG DOTNET_VERSION=10.0

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG TARGETARCH
WORKDIR /src
# Restore first (cached until a project or package version changes).
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY .config/dotnet-tools.json .config/
COPY src/CustomGolfStats.Domain/CustomGolfStats.Domain.csproj src/CustomGolfStats.Domain/
COPY src/CustomGolfStats.Api/CustomGolfStats.Api.csproj src/CustomGolfStats.Api/
RUN dotnet tool restore \
 && dotnet restore src/CustomGolfStats.Api/CustomGolfStats.Api.csproj -a "$TARGETARCH"
COPY src/CustomGolfStats.Domain/ src/CustomGolfStats.Domain/
COPY src/CustomGolfStats.Api/ src/CustomGolfStats.Api/
# The OpenAPI document is generated (and committed) by normal builds; the image builds don't need it.
ENV OpenApiGenerateDocuments=false \
    DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN dotnet publish src/CustomGolfStats.Api/CustomGolfStats.Api.csproj -c Release -a "$TARGETARCH" --no-restore \
      -p:UseAppHost=false -o /out/api

# An EF Core migrations bundle: one executable that applies every pending migration and exits. It finds the database
# through the design-time factory, which reads ConnectionStrings__Postgres from the environment (never the command line).
FROM build AS bundle
ARG TARGETARCH
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) echo "unsupported arch $TARGETARCH" >&2; exit 1 ;; esac \
 && dotnet ef migrations bundle --project src/CustomGolfStats.Api --configuration Release \
      --target-runtime "$rid" --output /out/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
# curl for the container healthcheck only.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1
WORKDIR /app

FROM runtime AS migrator
COPY --from=bundle /out/efbundle ./efbundle
# The non-root user the .NET images ship with.
USER $APP_UID
ENTRYPOINT ["./efbundle"]

FROM runtime AS api
COPY --from=build /out/api ./
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "CustomGolfStats.Api.dll"]
