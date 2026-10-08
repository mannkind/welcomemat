# syntax=docker/dockerfile:1
# Compile wwwroot/app.js here; the .NET SDK image has no node.
FROM --platform=$BUILDPLATFORM node:24-alpine AS client
WORKDIR /client

COPY package.json package-lock.json tsconfig.json ./
RUN npm ci --no-audit --no-fund

COPY WelcomeMat/Scripts/ WelcomeMat/Scripts/
RUN npx tsc

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302-alpine3.24 AS build
WORKDIR /src

ARG TARGETARCH

COPY WelcomeMat/WelcomeMat.csproj WelcomeMat/
RUN dotnet restore WelcomeMat/WelcomeMat.csproj -a $TARGETARCH

COPY WelcomeMat/ WelcomeMat/
COPY --from=client /client/WelcomeMat/wwwroot/app.js WelcomeMat/wwwroot/app.js

RUN dotnet publish WelcomeMat/WelcomeMat.csproj \
    -c Release \
    -a $TARGETARCH \
    --self-contained false \
    --no-restore \
    -p:SkipClientBuild=true \
    -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-alpine3.24-extra AS final
WORKDIR /app

COPY --from=build /app/publish .

RUN mkdir -p /app/state/images /app/state/data-protection-keys && \
    chown -R app:app /app/state && \
    apk add --no-cache sqlite

VOLUME /app/state
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD host="${BASE_URL:-http://localhost:8080}"; \
      host="${host%%,*}"; \
      host="${host#http://}"; \
      host="${host#https://}"; \
      host="${host%%/*}"; \
      host="${host%%:*}"; \
      wget -qO- --header="Host: ${host}" http://127.0.0.1:8080/healthz >/dev/null || exit 1

USER app

ENTRYPOINT ["dotnet", "WelcomeMat.dll"]
