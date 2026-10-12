# Online Banking · ASP.NET Core MVC

An internet-banking site where customers open accounts, check balances and statements, and transfer money with a transaction PIN, and bank staff run everything from an admin console. The server renders every page with Razor views, so there's no separate frontend app.

[![CI](https://github.com/code-say/Online-Banking/actions/workflows/ci.yml/badge.svg)](https://github.com/code-say/Online-Banking/actions/workflows/ci.yml)

- **Live demo:** https://online-banking.onrender.com

> Hosted on Render's free tier, which sleeps when idle, so the first visit can take up to a minute. Demo data resets when the server restarts.

**Demo logins**

| Role | Email | Password |
|---|---|---|
| Customer | `customer@onlinebank.demo` | `Customer@12345` |
| Admin | `admin@onlinebank.demo` | `Admin@12345` |

The demo customer's transaction PIN is `2580`. Try sending money to account `5010 0098 7654`.

## Features

**Customers**
- Sign up, then apply for a savings or current account with KYC details (only the last 4 Aadhaar digits are stored)
- Dashboard with balances across accounts and recent activity
- Statements with date and money-in/out filters, paging, totals, CSV download and print
- Fund transfers to any account in the bank, with a masked beneficiary-name check, a transaction PIN and a printable receipt
- Profile: change password, set or change the transaction PIN

**Banking Assistant (AI agent)**
- A chat panel on customer pages: "What's my balance?", "How much did I spend last month?", "Send ₹500 to 5010 0098 7654"
- Built on **Microsoft.Extensions.AI** (`IChatClient` + function calling) with **Google Gemini** through Google's `Google.GenAI` .NET SDK; swapping providers is a one-line change in `Program.cs`
- Tools call the same `BankingService` the pages use, always scoped to the signed-in customer: `get_accounts`, `get_transactions`, `get_spending_summary`, `lookup_payee`, `prepare_transfer`
- **The agent can't move money.** `prepare_transfer` validates the transfer and shows a confirmation card; the customer reviews it on the Transfer page and enters their PIN themselves. The model never sees PINs or passwords
- Guardrails: transaction remarks written by other customers are marked as untrusted data (prompt-injection defence); conversation history is kept on the server, not trusted from the browser; per-customer daily message limit; provider errors are logged, never shown
- Tests drive the real function-calling pipeline with a scripted `IChatClient`, so CI needs no API key

**Bank admin** (`/Admin`)
- Overview: customers, accounts, total deposits, today's volume, latest transactions
- Review applications: approve (opens the account with a new number and an opening deposit) or reject with a reason
- Accounts: search, statements, cash deposits and withdrawals at the counter, freeze, unfreeze, close
- Customers and the contact-form inbox

## Banking rules

All money movement goes through `BankingService`:

- A transfer writes two ledger lines (debit and credit with a shared reference) and updates both balances in **one database transaction**
- **Optimistic concurrency** on account balances, so two requests can't spend the same money
- **Idempotent transfers**: each form carries a request id with a unique index, so a double-click or refresh never sends twice
- Insufficient-funds check, ₹2,00,000 **daily transfer limit** per account (India time), max ₹1 crore per transaction, two decimal places
- **Transaction PIN** (hashed); 3 wrong attempts lock transfers for 15 minutes; obvious PINs like 1111 and 1234 are refused
- Frozen or closed accounts can't send or receive; an account can't be closed while it holds money
- Money is stored as whole paise (integers) so SQLite compares and sorts it exactly

## Security

- Cookie authentication with roles (Customer, Admin), 20-minute sliding sessions, HttpOnly + SameSite cookies
- Passwords and PINs hashed with ASP.NET Core Identity's `PasswordHasher`
- Antiforgery tokens required on every POST
- Content-Security-Policy (no inline scripts or styles), `X-Frame-Options: DENY`, `nosniff`, HSTS
- Customers can only see their own accounts (other ids return 404); CSV export neutralises spreadsheet formulas

## Tech stack

| Layer | Technology |
|---|---|
| Web | ASP.NET Core MVC (.NET 10), Razor views, tag helpers, an Admin area |
| AI | Microsoft.Extensions.AI (function calling), Google Gemini via Google.GenAI |
| Data | Entity Framework Core, SQLite |
| UI | Hand-written CSS and a little vanilla JavaScript (every page works without it) |
| Tests | xUnit with `WebApplicationFactory` (banking rules + HTTP flows), Playwright end-to-end flow |
| Delivery | Docker, GitHub Actions, Render |

## Project structure

```
src/OnlineBanking.Web/
  Controllers/        Home, Account (sign in/up), Dashboard, Accounts (statements), Transfer, Applications, Profile
  Areas/Admin/        Dashboard, Applications, Accounts, Customers, Messages (controllers + views)
  Services/           BankingService (all money rules), AuthService
  Services/Assistant/ BankingAssistant (prompt, history, limits), AssistantTools (the agent's functions)
  Entities/  Data/    EF Core model, DbContext, demo seed data
  Models/             View models with validation
  Views/              Razor views
  wwwroot/            CSS, JS, icon
tests/OnlineBanking.Tests/   xUnit tests
e2e/flow.mjs                 Playwright browser flow (run in CI)
Dockerfile · render.yaml · .github/workflows/ci.yml
```

## Run locally

Requires the .NET 10 SDK.

```bash
dotnet run --project src/OnlineBanking.Web
# http://localhost:5090
```

To switch on the assistant, create a free API key in [Google AI Studio](https://aistudio.google.com/apikey) and set it before running (the model can be changed with `Assistant__Model`, default `gemini-3.8-flash`):

```bash
export Assistant__ApiKey="your-key"     # PowerShell: $env:Assistant__ApiKey="your-key"
```

Run the tests with `dotnet test`.

## History

This started in April 2022 as an ASP.NET MVC 5 project on .NET Framework, with Entity Framework 6 (database-first) and SQL Server LocalDB ([original code in the git history](https://github.com/code-say/Online-Banking/tree/2f75845)). In 2026 it was rebuilt on ASP.NET Core MVC so it runs on Linux, with real balances, transfers, tests and deployment.
