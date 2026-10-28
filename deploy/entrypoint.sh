#!/bin/sh
set -eu

# Railway exposes PostgreSQL as PG* variables. Explicit ASP.NET connection strings still take priority.
# PGUSER owns the schema and runs migrations. The running app connects as APP_DB_USER, a data-only role,
# so the append-only ledgers (stock movements, order history, activity log) cannot be rewritten.
if [ -n "${PGHOST:-}" ]; then
  db="Host=${PGHOST};Port=${PGPORT:-5432};Database=${PGDATABASE};SSL Mode=Prefer;Trust Server Certificate=true"
  if [ -z "${ConnectionStrings__Migrations:-}" ]; then
    export ConnectionStrings__Migrations="${db};Username=${PGUSER};Password=${PGPASSWORD}"
  fi
  if [ -z "${ConnectionStrings__Default:-}" ]; then
    export ConnectionStrings__Default="${db};Username=${APP_DB_USER:-$PGUSER};Password=${APP_DB_PASSWORD:-$PGPASSWORD}"
  fi
fi
if [ -z "${ConnectionStrings__Migrations:-}" ] && [ -n "${ConnectionStrings__Default:-}" ]; then
  export ConnectionStrings__Migrations="${ConnectionStrings__Default}"
fi

: "${ConnectionStrings__Default:?Set ConnectionStrings__Default or Railway PGHOST/PGDATABASE/PGUSER/PGPASSWORD variables}"
: "${ConnectionStrings__Migrations:?Set ConnectionStrings__Migrations or Railway PostgreSQL variables}"

export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-8080}"

dotnet AlaraERP.Api.dll initialize
exec dotnet AlaraERP.Api.dll
