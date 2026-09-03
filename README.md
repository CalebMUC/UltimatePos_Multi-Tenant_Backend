<div align="center">

# Ultimate POS v2

**Clean Architecture POS & manufacturing backend, built first for a Kenyan agrovet client**

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-316192?style=flat&logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![Status](https://img.shields.io/badge/status-scaffolding-yellow)]()
[![License](https://img.shields.io/badge/License-Proprietary-red.svg)](LICENSE)

[Business Problem](#-business-problem) •
[Architecture](#-architecture) •
[Business Flow](#-business-flow) •
[Tech Stack](#-tech-stack) •
[Quick Start](#-quick-start) •
[API Reference](#-api-reference) •
[Roadmap](#-roadmap)

</div>

---

## 🏭 Business Problem

The first client on this system is an agrovet manufacturer — animal supplements and animal feed, sold mainly to smaller retail agrovets. Three problems drove the rebuild:

| Problem | Reality | This System Solves It With |
|---|---|---|
| **Production cost is a guess** | Raw inputs get mixed to a formula and packed to order, but nothing computes what a batch actually cost to make | `Formula` + `ProductionBatch` — materials, labour, and packaging cost calculated per batch, yielding a real per-unit cost |
| **Payment is compliance-gated** | Buyers have started refusing to pay until they receive a KRA eTIMS receipt | Every sale submits to eTIMS (VSCU) and tracks the invoice control number end to end |
| **Buyers pay on account** | Buyers don't pay immediately; balances were tracked informally | `Customer` + `CustomerLedgerEntry` — running balance, statements, credit limits |

### Who Uses This

**Cashiers** — record sales at the till, open/close till sessions.
**Production staff** — run batches against a formula, record actual yield.
**Supervisors** — approve till closures, review production costs against price lists.
**Admin** — manage users, roles, permissions, price lists, formulas.

This is an internal system for one business, not a multi-tenant marketplace — no two-sided user model here.

---

## 🏗️ Architecture

### Clean Architecture — a deliberate, reconsidered choice

An earlier plan for this rebuild deliberately skipped a full architectural split in favor of just adding a service layer to the existing codebase, to move faster for the client. That decision was reversed once the scope of what's being built became clear — Formulas/Production is a genuinely separate domain from Sales/Till, and letting the compiler enforce that boundary is worth more here than it would be on a smaller system.

```
UltimatePos.Api             Controllers · Middleware · Swagger · composition root
UltimatePos.Application     Use cases · repository interfaces · DTOs, by feature
UltimatePos.Domain          Entities · enums · domain exceptions — zero dependencies
UltimatePos.Infrastructure  EF Core · M-Pesa · eTIMS · JasperReports · Quartz jobs
```
Dependency rule: outer layers depend on inner layers only. `Domain` has zero external dependencies.

| Factor | Why Clean Architecture here |
|---|---|
| Root cause fix | The original codebase's core problem was repositories mixing data access, business rules, and auth logic in one class — physical project boundaries prevent that from recurring |
| Domain complexity | Formulas/Production, eTIMS compliance, and customer credit are each substantial enough to need real separation, not just folders |
| Ported Auth | The existing JWT + dynamic permission system is being reused as-is — Clean Architecture gives it a clean home (`Infrastructure/Auth` + `Api/Auth`) instead of bolting it onto whatever else is nearby |
| Solo/part-time cost | Real — this added several weeks versus the lighter service-layer plan. Accepted as a front-loaded cost since it's cheapest to pay now, before more code exists |

No microservices extraction plan exists for this system, and none is needed at this scale — one business, one deployment. Noted here only because it's the natural next question after "why Clean Architecture": the layering is about internal maintainability, not a step toward splitting services later.

---

## 📦 Business Flow

### Sale / Checkout

```
CASHIER                    API                              
   │                        │
   │── POST /sales ────────►│
   │  (lines, customerId,   │── Validate stock at base unit ──►
   │   payment method)      │   (Box sale → converts to Packets)
   │                        │── Snapshot price from active tier ──►
   │                        │── Post StockMovement(Sale) ──►
   │                        │── Post CustomerLedgerEntry, if on account ──►
   │                        │── Queue eTIMS submission (async) ──►
   │◄── Sale confirmed ─────│
   │   (etimsStatus: Pending)
   │                        │
   │── GET /sales/{id}/etims-status ──►│
   │◄── Submitted + KRA invoice number ─│
```

### Production Batch

```
Formula (versioned recipe)
   │
   ▼
ProductionBatch created ── Status: Planned
   │
   ▼
PATCH .../start ── posts ProductionConsumption movements at planned quantities
   │
   ▼
PATCH .../complete ── records actual yield + any consumption adjustments
   │
   ▼
Cost computed: materials + labour + packaging + overhead → unit cost
   │
   ▼
Posts ProductionOutput movement — finished goods stock increases by actual yield
```

---

## 🔐 Authentication

Ported from the original Ultimate POS API, not rebuilt — JWT bearer tokens plus a dynamic, permission-based authorization policy provider (`PERMISSION:<Resource>.<Action>`, e.g. `PERMISSION:Products.Create`), resolved against a `permissions` claim on the token. This was the one part of the original system that was already well designed. **Not yet re-integrated into this project** — see Roadmap.

```json
// POST /api/v1/auth/login  (planned shape, matches the ported system)
{ "phoneNumber": "...", "password": "..." }

// response
{
  "success": true,
  "data": {
    "accessToken": "eyJ...",
    "user": { "id": 12, "roles": ["Cashier"], "permissions": ["Sales.Create", "Till.Open"] }
  }
}
```

---

## 🛠️ Tech Stack

| Layer | Technology | Why This Choice |
|---|---|---|
| API | ASP.NET Core 8 | Matches the existing team's stack; strong typing, mature tooling |
| ORM | EF Core 8 + Npgsql | PostgreSQL already the target; migrations, LINQ-to-SQL |
| Database | PostgreSQL | ACID transactions for stock/payment consistency; JSONB available if ever needed |
| Auth | JWT + dynamic permission policies | Already well-designed in the original system — ported, not rebuilt |
| Payments | M-Pesa Daraja | Primary payment method in the target market |
| Compliance | KRA eTIMS (VSCU) | Legally required for VAT-registered invoicing in Kenya; currently blocking buyer payment |
| Background jobs | Quartz.NET | Notification and settlement jobs, scheduled not event-driven — no message broker needed at this scale |
| Reporting | JasperReports | Carried over from the original system |
| Logging | Serilog | Structured logs |

No Redis, RabbitMQ, or SignalR — those solve problems (distributed caching, async fan-out, live push) this system doesn't have yet at one-business scale. Worth reconsidering only if that changes.

---

## 🗃️ Database Design

### Core Schema

```
Product              ── Sku, ItemType (RawMaterial|Packaging|FinishedGood), TaxClassification
├── ProductUnitConversion   ── Packet ↔ Box, etc.
├── ProductPriceTier        ── Retail/Wholesale, dated
Formula               ── versioned recipe, per finished Product
├── FormulaIngredient
ProductionBatch
├── ProductionBatchConsumption
StockMovement          ── Purchase | Sale | ProductionConsumption | ProductionOutput | Adjustment
Sale
├── SaleLine
├── Payment
Customer
├── CustomerLedgerEntry
PurchaseOrder
├── PurchaseOrderItem
├── GoodsReceipt
AuditLog
```

### Key Indexes (planned)

```sql
-- Stock lookups: current stock for an item is SUM(Quantity) filtered like this
CREATE INDEX idx_stockmovement_item_location ON StockMovement(ItemId, LocationId);

-- Formula lookup by finished product
CREATE INDEX idx_formula_product ON Formula(ProductId, IsActive);

-- Customer statement / aging queries
CREATE INDEX idx_customerledger_customer_date ON CustomerLedgerEntry(CustomerId, Date);

-- Low-stock alerts on raw materials feeding active formulas
CREATE INDEX idx_product_reorder ON Product(ItemType, ReorderLevel) WHERE ItemType <> 'FinishedGood';
```

### Money Handling

Stored as `decimal(18,2)`, not `float`/`double` — .NET's `decimal` type doesn't have the binary floating-point rounding problem those do, so there's no need for an integer-cents workaround here the way there would be in a language without a true decimal type.

---

## 🔒 Security

| Feature | Status |
|---|---|
| Password hashing (PBKDF2-HMACSHA256) | 🚧 Planned — porting from original, raising iteration count while we're in there |
| JWT bearer tokens | 🚧 Planned — ported, not yet re-integrated |
| Permission-policy authorization, default-deny | 🚧 Planned — this is the fix for the original system's biggest gap (whole modules with no auth at all) |
| Input validation (DataAnnotations/FluentValidation) | 🚧 Planned |
| SQL injection protection (EF Core parameterized queries) | ✅ By construction — no raw SQL anywhere in the design |
| HTTPS enforcement | 🚧 Planned |
| Secrets management (user-secrets / env vars, never committed) | ✅ In place from the first commit |
| Optimistic concurrency on stock | 🚧 Planned — direct fix for a real race condition in the original system |

Marked honestly — this project is at the scaffolding stage. Nothing above is a false claim of completeness.

---

## 🇰🇪 Kenya-Specific Implementation

### M-Pesa (planned pattern)
```csharp
await _mpesaClient.InitiateStkPushAsync(new StkPushRequest
{
    PhoneNumber = customer.PhoneNumber,
    Amount = sale.Total,
    AccountReference = sale.Id.ToString()
});
```

### eTIMS (planned pattern)
```csharp
await _etimsClient.SubmitInvoiceAsync(new EtimsInvoiceRequest
{
    SaleId = sale.Id,
    Lines = sale.Lines.Select(l => new EtimsLine(l.ProductId, l.Quantity, l.UnitPrice, l.TaxClassification))
});
```

### Tax classification
Animal feed is VAT-exempt/zero-rated under current KRA rules; supplements/premixes aren't automatically covered by the same classification and need confirming per SKU. `Product.TaxClassification` is a field, not a hardcoded rate, specifically because of this.

---

## 🚀 Quick Start

```powershell
dotnet user-secrets init --project src/UltimatePos.Api
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<local connection string>" --project src/UltimatePos.Api
dotnet build
dotnet run --project src/UltimatePos.Api
```

Swagger at `/swagger`. Honest current state: this builds and runs an empty API. No DbContext, no auth, no endpoints yet — see Roadmap.

---

## 📡 API Reference

Full request/response contracts in the architecture guide. Endpoint map:

| Area | Endpoints |
|---|---|
| Auth | `/api/v1/auth/login`, `/register`, `/roles`, `/permissions`, `/users` |
| Catalog | `/api/v1/products`, `/categories`, `/units-of-measure`, `/products/{id}/price-tiers` |
| Purchasing | `/api/v1/suppliers`, `/purchase-orders`, `/purchase-orders/{id}/receipts`, `/purchase-invoices` |
| Formulas & Production | `/api/v1/formulas`, `/production-batches`, `/production-batches/{id}/start`, `/complete` |
| Sales & Till | `/api/v1/till-sessions`, `/sales` |
| Customers | `/api/v1/customers`, `/customers/{id}/statement`, `/customers/{id}/payments` |
| eTIMS | `/api/v1/sales/{id}/etims-status`, `/etims/retry`, `/etims/callback` |
| Finance | `/api/v1/expenses`, `/expense-categories` |
| Reports | `/api/v1/reports/production-costs`, `/margins`, `/customer-aging`, `/dashboard/summary` |

---

## 📊 Scale

Not the relevant concern right now — this is one business's internal system, not a multi-tenant marketplace. A handful of concurrent till sessions and a few hundred sales a day is the realistic load. Worth revisiting only if this ever grows into serving multiple businesses.

---

## 🧪 Testing

```bash
dotnet test tests/UltimatePos.Api.Tests/
```

Target, not current state: Domain logic close to 100% (pure functions, no mocks needed), Application use cases with mocked repositories, Infrastructure covered by a smaller set of integration tests against a real Postgres instance.

---

## 🚢 Deployment

Not built yet — deliberately deferred until closer to actually deploying (see prior discussion). Planned shape once it's needed:

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["src/UltimatePos.Api/UltimatePos.Api.csproj", "src/UltimatePos.Api/"]
RUN dotnet restore "src/UltimatePos.Api/UltimatePos.Api.csproj"
COPY . .
RUN dotnet publish "src/UltimatePos.Api/UltimatePos.Api.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UltimatePos.Api.dll"]
```

Env vars, once containerized: `ConnectionStrings__DefaultConnection`, `JwtSettings__Key`, `JwtSettings__Secret`, `Mpesa__ConsumerKey`, `Mpesa__ConsumerSecret`, `Etims__BaseUrl`.

---

## 🗺️ Roadmap

- [x] Solution scaffold — four-project Clean Architecture structure, git initialized
- [ ] Auth — port `User`/`Role`/`Permission` + JWT + policy provider
- [ ] Catalog — Products, Categories, Units, Price Tiers
- [ ] Purchasing — Suppliers, Purchase Orders, Goods Receipts
- [ ] Sales & Till — checkout flow, stock deduction
- [ ] Customers & Credit
- [ ] eTIMS — priority once core sales work, since this is currently blocking buyer payment
- [ ] Formulas & Production — the largest new domain
- [ ] Reports, Finance, hardening

---

## 📁 Project Structure

```
UltimatePos.sln
└── src/
    ├── UltimatePos.Domain/
    ├── UltimatePos.Application/
    ├── UltimatePos.Infrastructure/
    └── UltimatePos.Api/
tests/
└── UltimatePos.Api.Tests/
```

---

## 📄 Architecture Decision Records

| Decision | Choice | Rationale |
|---|---|---|
| Full Clean Architecture vs. service layer only | Clean Architecture | Reversed from an earlier lighter plan once the domain (Formulas/Production) proved substantial enough to need real boundaries |
| Money as `decimal(18,2)` vs. integer cents | `decimal` | .NET's decimal type has no binary floating-point rounding issue; no need for the integer-cents workaround |
| Stock as a mutable column vs. a movement ledger | `StockMovement` ledger | Production adds a third stock-changing workflow beyond purchase/sale — a flat column can't explain why stock changed after the fact |
| eTIMS priority vs. Formula/Production priority | eTIMS first | Buyers are currently refusing to pay without it — revenue-blocking beats internal cost-visibility |
| Auth: port vs. rebuild | Port | The original permission-policy design was already sound; rebuilding it would be redoing already-good work |
| Multi-tenancy now vs. later | Later | One real client today; retrofitting a `BusinessId` later is expensive but not as expensive as building SaaS infrastructure nobody's using yet |

---

## 👤 Author

**Caleb Muchiri**
Full-Stack Software Engineer · Nairobi, Kenya

[![LinkedIn](https://img.shields.io/badge/LinkedIn-Connect-0077B5?style=flat&logo=linkedin)](https://linkedin.com/in/caleb-muchiri-909ba6266)
[![GitHub](https://img.shields.io/badge/GitHub-Follow-181717?style=flat&logo=github)](https://github.com/CalebMUC)

---

## 📄 License

Proprietary © 2026
