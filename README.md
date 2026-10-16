# ÀLara Line ERP

Internal operations system for **ÀLara Line**, a Lebanese haircare brand: catalogue, stock with batch and expiry
tracking, customer profiles and loyalty analytics, orders from WhatsApp / Instagram / the website, courier delivery, and cash-on-delivery reconciliation.
It is an internal tool for employees, separate from the public website [alaraline.co](https://alaraline.co).
The future React storefront will read the same catalogue.

| | |
|---|---|
| Backend | ASP.NET Core 10 Web API (controllers), EF Core 10 + Npgsql, ASP.NET Core Identity, FluentValidation |
| Database | PostgreSQL 17, schemas `identity`, `catalog`, `inventory`, `sales` |
| Frontend | React 19 + TypeScript, Vite, React Router, TanStack Query, React Hook Form + Zod, Tailwind CSS 4, shadcn/ui (Radix), Lucide, Recharts |
| Tests | xUnit (unit, architecture, PostgreSQL integration), Vitest, Puppeteer smoke test |

Design and data model: **[docs/architecture.md](docs/architecture.md)**, which covers module boundaries, the ERD, the permission matrix and the order lifecycle.

---

## Architecture in one minute

A **modular monolith**: one API, one SPA and one database. Four layers, each a project:

```text
backend/src/
  AlaraERP.Domain          entities + business rules (no framework dependencies)
  AlaraERP.Application     use-case services, DTOs, validators   ─┐ folders per module:
  AlaraERP.Infrastructure  EF Core, Identity, storage, CLI tasks  │ Identity · Catalog · Inventory
  AlaraERP.Api             controllers, auth, errors, CLI entry  ─┘ Sales · Dashboard · Analytics
backend/tests/
  AlaraERP.UnitTests · AlaraERP.ArchitectureTests · AlaraERP.IntegrationTests
frontend/src/
  features/<module>/       pages per module    components/  shared UI    lib/  api client, formatting, queries
data/catalog/              the real catalogue, imported once (see below)
```

**Module rules**, enforced by `AlaraERP.ArchitectureTests`:
- Dependencies flow one way: Catalog ← Inventory ← Sales.
- Sales may change stock **only** through `Inventory.Contracts.IStockReservationService`.
- Dashboard and Analytics are read-only reporting modules and may read every module.

---

## Prerequisites

.NET SDK 10, Node 22+ (developed on 26), PostgreSQL 17, Chrome (only for the browser smoke test).

## Database setup (once)

The app never connects as a superuser. Create two roles and the databases as a superuser, choosing strong passwords:

```sql
CREATE ROLE alara_owner LOGIN PASSWORD '…' NOSUPERUSER NOCREATEROLE NOCREATEDB;  -- runs migrations
CREATE ROLE alara_app   LOGIN PASSWORD '…' NOSUPERUSER NOCREATEROLE NOCREATEDB;  -- runtime, data only
CREATE DATABASE alara_erp;   -- skip if it exists
REVOKE ALL ON DATABASE alara_erp FROM PUBLIC;
GRANT CONNECT, CREATE, TEMPORARY ON DATABASE alara_erp TO alara_owner;
GRANT CONNECT ON DATABASE alara_erp TO alara_app;
\c alara_erp
GRANT CREATE, USAGE ON SCHEMA public TO alara_owner;   -- for the migrations history table

CREATE DATABASE alara_erp_test OWNER alara_owner;      -- tests rebuild THIS database only
REVOKE ALL ON DATABASE alara_erp_test FROM PUBLIC;
GRANT CONNECT ON DATABASE alara_erp_test TO alara_app;
\c alara_erp_test
GRANT CREATE, USAGE ON SCHEMA public TO alara_owner;
```

The `migrate` command grants `alara_app` DML on every module schema, with default privileges so future
migrations are covered. It also revokes UPDATE/DELETE on the audit ledgers (`inventory.stock_movements`,
`sales.order_history`), so history cannot be rewritten even by the application.

> **Local note:** Homebrew PostgreSQL ships with `trust` authentication in `pg_hba.conf`, which means passwords
> are not checked locally. Production must use `scram-sha-256`, TLS, and no `trust` lines.

## Configuration and secrets

Secrets never go in the repository. In development they are kept in **dotnet user-secrets**, which the API and the integration tests share:

```bash
cd backend
dotnet user-secrets set --project src/AlaraERP.Api "ConnectionStrings:Default"    "Host=localhost;Database=alara_erp;Username=alara_app;Password=…"
dotnet user-secrets set --project src/AlaraERP.Api "ConnectionStrings:Migrations" "Host=localhost;Database=alara_erp;Username=alara_owner;Password=…"
dotnet user-secrets set --project src/AlaraERP.Api "ConnectionStrings:TestApp"    "Host=localhost;Database=alara_erp_test;Username=alara_app;Password=…"
dotnet user-secrets set --project src/AlaraERP.Api "ConnectionStrings:TestOwner"  "Host=localhost;Database=alara_erp_test;Username=alara_owner;Password=…"
```

In production, use environment variables. [`.env.example`](.env.example) lists every setting.

## First run

```bash
cd backend
dotnet tool restore                                         # local dotnet-ef
dotnet run --project src/AlaraERP.Api -- migrate            # apply migrations, grants, sync roles/permissions
dotnet run --project src/AlaraERP.Api -- import-catalog "$PWD/../data/catalog/alaraline-catalog.json"
dotnet run --project src/AlaraERP.Api -- bootstrap-admin    # first Super Admin; password typed hidden
```

- `import-catalog` loads the real catalogue: 28 products, 8 bundles, 5 categories and the `MAIN` warehouse.
  - It is idempotent and additive: products are matched by their web slug, and nothing that already exists is overwritten.
  - It creates no stock, costs or orders. Those must come from real counts.
- `bootstrap-admin` refuses to run once an active Super Admin exists. After that, employees are created in the app under **Employees**.

## Running

```bash
# terminal 1 — API on http://localhost:5190 (OpenAPI document: /openapi/v1.json in Development)
cd backend && dotnet run --project src/AlaraERP.Api

# terminal 2 — SPA on http://localhost:5173 (proxies /api to the API: same origin, no CORS)
cd frontend && npm install && npm run dev
```

Open http://localhost:5173 and sign in. Sessions survive refreshes and restarts of either server, because the
data lives in PostgreSQL and the session lives in an HttpOnly cookie.

Employees can sign in with either their username or email address. For local account setup without storing a
password in source control, run the interactive command below after `migrate`:

```bash
cd backend
dotnet run --project src/AlaraERP.Api -- provision-user <username> <email> <first-name> <last-name> <role>
```

Local Development accepts passwords of at least eight characters. Testing and Production retain the stronger
ten-character mixed-case policy.

## Tests and checks

```bash
cd backend && dotnet test          # unit + architecture + integration (PostgreSQL alara_erp_test)
cd frontend && npm run check       # typecheck + lint + unit tests
cd frontend && npm run build
```

The integration tests rebuild the **test** database schema on each run and truncate data between tests. They refuse
to run against a database whose name doesn't end in `_test`.

The browser smoke test (`frontend/e2e/smoke.mjs`) drives the whole flow: sign in → receive stock → order a bundle
→ confirm → ship → deliver → inspect profit → withdraw a bundle → check the operational views, plus a phone-width check. **It creates data**, so point it at an
API that uses the test database:

```bash
ConnectionStrings__Default="<TestApp connection>" ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS=http://localhost:5191 dotnet run --project backend/src/AlaraERP.Api --no-launch-profile
ALARA_API_URL=http://localhost:5191 npm --prefix frontend run dev
E2E_PASSWORD=<test admin password> npm --prefix frontend run e2e
```

## Migrations

```bash
cd backend
dotnet ef migrations add <Name> --project src/AlaraERP.Infrastructure --startup-project src/AlaraERP.Api --output-dir Persistence/Migrations
dotnet run --project src/AlaraERP.Api -- migrate
```

`migrations add` never connects to a database. Migrations are applied only by `migrate`, which uses the owner role.
The API never migrates or resets the database at startup.

---

## Security model

- **Authentication.** ASP.NET Core Identity with cookie sessions.
  - The cookie is HttpOnly, `SameSite=Strict`, Secure in production, and lasts 8 hours (sliding).
  - Nothing is stored in localStorage, and there is no public sign-up.
- **CSRF.** Every POST/PUT/DELETE must carry `X-XSRF-TOKEN`, obtained from `GET /api/auth/csrf`. The token is bound to the signed-in user.
- **Brute force.**
  - An account locks for 15 minutes after 5 failed sign-ins.
  - Login is also rate-limited per IP.
  - An unknown username/email and a wrong password get the same answer.
- **Sessions are re-checked every minute.** Deactivation, password resets and role changes therefore reach open sessions within a minute.
- **Authorization.** Named permissions (e.g. `orders.create`) stored as role claims are checked by the API on every endpoint. The UI only hides what the API would refuse anyway.
- **No privilege escalation.**
  - You can only grant roles whose permissions you already hold.
  - Only a Super Admin grants Super Admin.
  - Nobody can change their own roles or deactivate themselves.
  - The last active Super Admin can't be removed.
- **Images.** The file type is checked from the file's bytes (JPEG/PNG/WebP only), up to 5 MB. Files are stored under generated keys outside the web root and served only to signed-in users.
- **Errors** use RFC 7807 ProblemDetails with a stable `code`. Stack traces and internal details are never returned.

### Roles

| Role | Can |
|---|---|
| Super Admin | everything, including employees and roles |
| Manager | catalogue, prices and costs; stock, thresholds, warehouses, orders, payments and courier cash, couriers, sales/profit reports; view employees |
| Inventory Manager | view products; receive, adjust and transfer stock |
| Sales Employee | manage products, bundles and categories; manage customers and orders; receive and view stock, receipts, warehouses and movements |
| Viewer | read-only catalogue, stock, orders and reports |

The full matrix is on the **Roles & permissions** page and in `Application/Common/Security/SystemRoles.cs`.
Roles are defined in code and synced to the database by `migrate`.

## Business rules (summary)

**Catalogue**
- Each product is one sellable item, and each scent is its own product.
- A bundle is its own product with its own price. Bundles with a scent choice are separate products per scent.
- Bundles hold no stock of their own: selling one reserves and deducts its components.
- Bundle availability = the minimum, across its components, of ⌊available ÷ quantity per bundle⌋.
- Products with history can't be deleted, only deactivated.
- Individual products may have a standard landed unit cost. Cost fields are visible only with `costs.view`.

**Stock**
- All stock lives in batches; products that track expiry need an expiry date when received.
- Order confirmation and transfers pick the **earliest-expiring** batch first.
- **Expired units are never shipped.** They must be written off with an "Expired" adjustment.
- Reserved stock can't be adjusted away or transferred.
- Each product has a low-stock alert threshold, set by a Manager.
- Every change writes an immutable movement record.
- A batch keeps its weighted unit cost, and every movement snapshots the cost used at that moment.
- Taking a bundle out for a PR box, gift, loss or damage deducts its component products; bundles never gain a separate stock balance.

**Order lifecycle**
- **Draft:** editable, stock is unchanged.
- **Confirmed:** stock is deducted immediately from FEFO batches. "Reopen" returns it.
- **Shipped:** courier and tracking details are recorded separately; stock is not changed again.
- **Delivered**, or **Returned** (stock goes back into the same batches it left from).
- **Cancelled** is possible only before shipping, and returns stock deducted at confirmation.
- Prices, names and bundle contents are frozen when the order is placed.

**Customers**
- Every new order links to a durable customer profile. A phone number already on file reuses the same customer, including equivalent Lebanese formats such as `+961 70…`, `70…`, and `03…`.
- Orders keep their own recipient and address snapshot, so editing a customer later never rewrites old orders.
- The Customers page shows lifetime orders, total ordered, total paid, average order value, and last activity.

**Cash on delivery**
- Delivered by a courier: the payment is *With courier* until a **courier hand-over** is recorded, which marks the orders *Paid* and records the courier's fees.
- Delivered by ÀLara Line staff: the order is *Paid* on delivery.

**Money**
- Amounts are in USD, using `decimal` / `NUMERIC(12,2)`.
- "Cash collected" on the dashboard counts **paid** orders only.
- Analytics reports sales on the confirmation date and excludes cancelled and returned orders. Cost of goods uses the exact batches deducted at confirmation.
- Loyalty analytics can rank customers by order count, total ordered, total paid, average order value, reorder frequency, or most recent order for the selected period.
- Bundle revenue is allocated across its component products by their full prices. Profit reports flag shipped units with no recorded cost.
