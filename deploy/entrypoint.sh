#!/bin/sh
set -eu

# Railway exposes PostgreSQL as PG* variables. Explicit ASP.NET connection strings still take priority.
if [ -z "${ConnectionStrings__Default:-}" ] && [ -n "${PGHOST:-}" ]; then
  export ConnectionStrings__Default="Host=${PGHOST};Port=${PGPORT:-5432};Database=${PGDATABASE};Username=${PGUSER};Password=${PGPASSWORD};SSL Mode=Prefer;Trust Server Certificate=true"
fi
if [ -z "${ConnectionStrings__Migrations:-}" ] && [ -n "${ConnectionStrings__Default:-}" ]; then
  export ConnectionStrings__Migrations="${ConnectionStrings__Default}"
fi

: "${ConnectionStrings__Default:?Set ConnectionStrings__Default or Railway PGHOST/PGDATABASE/PGUSER/PGPASSWORD variables}"
: "${ConnectionStrings__Migrations:?Set ConnectionStrings__Migrations or Railway PostgreSQL variables}"

export ASPNETCORE_URLS="http://0.0.0.0:${PORT:-8080}"

dotnet AlaraERP.Api.dll initialize
exec dotnet AlaraERP.Api.dll
