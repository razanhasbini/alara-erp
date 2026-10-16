# ÀLara Line ERP

Internal operations system for **ÀLara Line**, a Lebanese haircare brand: catalogue, stock with optional expiry
tracking, customer profiles and loyalty analytics, orders from WhatsApp / Instagram / the website, and standalone delivery-cost tracking.
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

**WhatsApp alerts (optional).** Until these are set, alerts are only recorded in the app's Alerts page with status "WhatsApp not connected". They use the Meta WhatsApp Cloud API:

```bash
dotnet user-secrets set --project src/AlaraERP.Api "WhatsApp:Enabled" "true"
dotnet user-secrets set --project src/AlaraERP.Api "WhatsApp:PhoneNumberId" "…"      # from Meta Business → WhatsApp → API setup
dotnet user-secrets set --project src/AlaraERP.Api "WhatsApp:AccessToken" "…"        # a permanent system-user token
dotnet user-secrets set --project src/AlaraERP.Api "WhatsApp:Recipients:0" "96170123456"
dotnet user-secrets set --project src/AlaraERP.Api "WhatsApp:TemplateName" "erp_alert"  # approved template with one {{1}} body parameter
dotnet user-secrets set --project src/AlaraERP.Api "App:FrontendUrl" "https://erp.example.com"  # link added to messages
```

Without a template, plain text messages only reach numbers that messaged the business in the last 24 hours (Meta's rule).

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

## Deploying on Railway

The root [Dockerfile](Dockerfile) builds the React app and ASP.NET API into one service. The API serves the SPA,
so browser sessions, CSRF protection, images and API calls all use one HTTPS origin.

1. Create a Railway project, add PostgreSQL, then add this GitHub repository as a service.
2. In the app service, reference the PostgreSQL service variables:

   ```env
   PGHOST=${{Postgres.PGHOST}}
   PGPORT=${{Postgres.PGPORT}}
   PGDATABASE=${{Postgres.PGDATABASE}}
   PGUSER=${{Postgres.PGUSER}}
   PGPASSWORD=${{Postgres.PGPASSWORD}}
   ```

3. Add a volume mounted at **`/app/App_Data`**. Product photos are stored under `/app/App_Data/uploads` and the
   sign-in encryption keys under `/app/App_Data/keys`. Without this volume, photos disappear and everyone is signed
   out on every redeploy.
4. Generate a public domain and set `App__FrontendUrl` to its full `https://...` address.
5. Set the initial account variables below. They are read only when an account does not yet exist, so restarts do
   not reset a password that was changed later.

   ```env
   Bootstrap__ImportCatalog=true
   Bootstrap__SuperAdmin__UserName=lara
   Bootstrap__SuperAdmin__Email=lara@alaraline.local
   Bootstrap__SuperAdmin__FirstName=Lara
   Bootstrap__SuperAdmin__LastName=Admin
   Bootstrap__SuperAdmin__Password=<temporary password>
   Bootstrap__Employee__UserName=diana
   Bootstrap__Employee__Email=diana@alaraline.local
   Bootstrap__Employee__FirstName=Diana
   Bootstrap__Employee__LastName=Employee
   Bootstrap__Employee__Password=<temporary password>
   ```

   The requested temporary eight-character password requires `Auth__SimpleDevelopmentPasswords=true`. Remove
   that setting after replacing both passwords with production-strength passwords.

The container runs the idempotent `initialize` command before every start. It applies pending migrations,
synchronizes permissions, imports missing catalogue records when enabled, and creates only missing initial users.
The committed [railway.json](railway.json) also checks `/health` before a deployment is marked healthy.

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

`migrations add` never connects to a database. Migrations are applied by `migrate`, which uses the owner role.
The Railway container runs the idempotent `initialize` command before starting the API; it applies pending
migrations and never resets the database.

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
| Manager | catalogue and prices; stock, thresholds, warehouses, orders, delivery costs, sales reports; view employees |
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
- **Reserved:** stock is held for the customer (it can't be sold to anyone else) until a chosen time, but nothing is deducted and no revenue or cost is counted. The order can still be edited. When the time is up an alert is raised (in **Alerts**, and on WhatsApp once connected); nothing is released automatically.
- **Sold** (`Confirmed` in the API): stock is deducted immediately from FEFO batches and the sale counts. "Back to reserved" undoes the sale and holds the stock again.
- Creating an order with too little free stock is refused, and the message names the reserved orders (number, quantity, customer, deadline) holding the rest.
- **Shipped:** marks that the sold order has left the business; stock and payment are not changed again.
- **Delivered**, or **Returned** (stock goes back into the same batches it left from).
- **Cancelled** is possible only before shipping; it releases a reservation or returns stock deducted when the order was sold.
- Prices, names and bundle contents are frozen when the order is placed.

**Customers**
- Every new order links to a durable customer profile. A phone number already on file reuses the same customer, including equivalent Lebanese formats such as `+961 70…`, `70…`, and `03…`.
- Orders keep their own recipient and address snapshot, so editing a customer later never rewrites old orders.
- The Customers page shows lifetime orders, total ordered, total paid, average order value, and last activity.

**Payment and delivery**
- A sold order is immediately *Paid*. Creating a sold order or changing a reservation to sold deducts stock and
  updates sales, cost of goods, cash received and profit in the same database transaction.
- A reserved order is *Unpaid* and holds stock. Moving a sold order back to reserved reverses the sale and stock
  deduction. Cancelling or returning a sold order makes its payment *Void*.
- Delivery companies and costs are managed on **Deliveries**. A cost may cover one day, week, month or invoice and
  is not linked to an order. Profit analytics subtract costs by their recorded date.

**Money**
- Amounts are in USD, using `decimal` / `NUMERIC(12,2)`.
- "Cash received" on the dashboard counts **paid** orders only; sold orders become paid immediately.
- Analytics reports sales on the confirmation date and excludes cancelled and returned orders. Cost of goods uses the exact batches deducted at confirmation.
- Loyalty analytics can rank customers by order count, total ordered, total paid, average order value, reorder frequency, or most recent order for the selected period.
- Bundle revenue is allocated across its component products by their full prices. Profit reports flag shipped units with no recorded cost.
