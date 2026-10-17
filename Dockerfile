FROM node:22-alpine AS frontend
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src/backend
COPY backend/ ./
RUN dotnet restore AlaraERP.slnx
RUN dotnet publish src/AlaraERP.Api/AlaraERP.Api.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# Npgsql probes for Kerberos (GSSAPI) on connect; without the library it logs a harmless error on every start.
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
COPY --from=backend /out ./
COPY --from=frontend /src/frontend/dist ./wwwroot
COPY data/catalog ./data/catalog
COPY deploy/entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh
ENV ASPNETCORE_ENVIRONMENT=Production \
    Storage__LocalPath=/app/App_Data/uploads \
    DataProtection__KeysPath=/app/App_Data/keys
EXPOSE 8080
ENTRYPOINT ["/app/entrypoint.sh"]
