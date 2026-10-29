# JP — Peer-to-Peer Marketplace API

A REST API for a peer-to-peer marketplace (think Vinted) where individuals buy and sell items, mostly pre-owned but new ones too, built with **ASP.NET Core 10**, **Entity Framework Core** and **PostgreSQL**.

## Tech stack

| Layer | Technology |
|---|---|
| Runtime / framework | .NET 10, ASP.NET Core Web API (controllers) |
| Data access | Entity Framework Core 10 + Npgsql |
| Database | PostgreSQL 17 |
| API docs | OpenAPI + [Scalar](https://scalar.com) UI |
| Tooling | `dotnet-ef` (local tool), Docker Compose (optional) |

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- PostgreSQL 17 — either via Docker **or** installed locally

### 1. Start PostgreSQL

**Option A — Docker**

```bash
docker compose up -d
```

**Option B — Local install (macOS / Homebrew)**

```bash
brew install postgresql@17
brew services start postgresql@17
export PATH="/opt/homebrew/opt/postgresql@17/bin:$PATH"

psql postgres -c "CREATE USER jp_marketplace WITH PASSWORD 'jp_marketplace' CREATEDB;"
createdb -O jp_marketplace jp_marketplace
```

The default connection string lives in `src/JP.Api/appsettings.Development.json`:

```
Host=localhost;Port=5432;Database=jp_marketplace;Username=jp_marketplace;Password=jp_marketplace
```

These are local development credentials only. Real secrets (JWT keys, payment keys) will go through `dotnet user-secrets`, never in the repository.

### 2. Run the API

```bash
dotnet run --project src/JP.Api --launch-profile http
```

In the Development environment the app applies the EF Core migrations and inserts demo data (categories, two users, three items) on startup.

- Interactive API docs: <http://localhost:5088/scalar>
- OpenAPI spec: <http://localhost:5088/openapi/v1.json>

## API

| Method | Endpoint | Description |
|---|---|---|
| `GET` | `/api/categories` | List categories (two-level hierarchy) |
| `GET` | `/api/items` | Search the catalogue (filters, sorting, pagination) |
| `GET` | `/api/items/{id}` | Item details |
| `POST` | `/api/items` | Create a listing |
| `PUT` | `/api/items/{id}` | Update a listing (rejected if already sold) |
| `DELETE` | `/api/items/{id}` | Delete a listing |

**Catalogue query parameters** (`GET /api/items`)

| Parameter | Example | Notes |
|---|---|---|
| `q` | `q=robe` | Case-insensitive match on title or brand |
| `categoryId` | `categoryId=2` | Includes sub-categories |
| `minPrice` / `maxPrice` | `minPrice=10000` | |
| `condition` | `condition=NewWithTags` | `NewWithTags`, `NewWithoutTags`, `VeryGood`, `Good`, `Satisfactory` |
| `sort` | `sort=price_asc` | `newest` (default), `price_asc`, `price_desc` |
| `page` / `pageSize` | `page=2&pageSize=20` | `pageSize` max 100 |

Example:

```bash
curl "http://localhost:5088/api/items?q=nike&sort=price_asc"
```

## Project structure

```
src/JP.Api/
├── Controllers/   HTTP endpoints (ItemsController, CategoriesController)
├── Dtos/          Request/response models, kept separate from entities
├── Entities/      Domain model: User, Category, Item, ItemPhoto, enums
├── Data/          AppDbContext (EF Core mapping) and demo seed data
├── Migrations/    EF Core migrations
└── Program.cs     Service registration and HTTP pipeline
```

## Design notes

- **DTOs instead of entities** in the API, so the public contract is independent from the database schema and requests are validated with data annotations.
- **Projections to DTOs** (`.Select(...)`) so EF Core only reads the columns it needs, and `AsNoTracking()` on read-only queries.
- **Enums stored as text** in the database for readable data.
- **Database-level constraints**: unique indexes on email, username and category slug; restricted deletes on categories.

## Useful commands

```bash
dotnet tool restore                          # installs dotnet-ef (local tool)
cd src/JP.Api
dotnet ef migrations add <MigrationName>     # after changing an entity
dotnet ef database update                    # apply migrations manually
```

## Roadmap

**Step 2 — Accounts & security**
- [ ] ASP.NET Core Identity + JWT authentication
- [ ] Seller taken from the token instead of the request body
- [ ] Only the owner can edit or delete a listing
- [ ] Public seller profile

**Step 3 — Marketplace core**
- [ ] Photo upload (local storage, then cloud blob storage)
- [ ] Favourites
- [ ] Real-time buyer ↔ seller chat with SignalR
- [ ] Price offers

**Step 4 — Purchase flow**
- [ ] Order state machine: `Available → Reserved → Sold`
- [ ] Stripe payment (test mode) with idempotent webhook
- [ ] Automatic reservation expiry with a `BackgroundService`
- [ ] Ratings and reviews

**Step 5 — Production quality**
- [ ] Unit and integration tests (xUnit, Testcontainers)
- [ ] CI with GitHub Actions
- [ ] Dockerfile and deployment
- [ ] Next.js front end