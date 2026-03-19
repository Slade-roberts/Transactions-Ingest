# TransactionsIngest

A .NET 10 console app for ingesting retail transactions from a mocked last-24-hour snapshot.

## What it does

- Runs once (no scheduler in the app).
- Loads an unordered JSON snapshot from `mock-data.json`.
- Upserts by `TransactionId`.
- Updates existing records only when tracked values changed.
- Writes one audit row per changed field.
- Marks records as `Revoked` when they are missing from the current snapshot but still within the last 24 hours.
- Optionally marks records older than 24 hours as `Finalized`.
- Skips all updates on finalized records.
- Wraps each ingestion run in one database transaction.

## Tech stack

- .NET 10
- EF Core (code-first)
- SQLite
- Configuration via `appsettings.json`
- xUnit tests

## Project structure

- `src/TransactionsIngest`
  - `Program.cs` (startup + DI)
  - `Data/AppDbContext.cs`
  - `Models/*`
  - `Services/*`
  - `appsettings.json`
  - `mock-data.json`
- `tests/TransactionsIngest.Tests`
  - `IngestTests.cs`

## Build and run

```bash
dotnet build TransactionsIngest.slnx
dotnet run --project src/TransactionsIngest
```

## Run tests

```bash
dotnet test TransactionsIngest.slnx
```

## Configuration

`src/TransactionsIngest/appsettings.json`

- `ConnectionStrings:Default` - SQLite connection string
- `Feed:Path` - mock snapshot path
- `Ingest:FinalizeOlderThan24Hours` - optional finalization switch
- `CardHash:Enabled` - whether card hash is stored
- `Api:BaseUrl` and `Api:UseMockFeed` - placeholders for real API wiring

## Data notes

- `TransactionId` is the stable key used for upsert.
- Raw card number is not stored.
- Stored card fields: `CardLast4` and optional `CardHash`.
- `LocationCode` and `ProductName` are capped at 20 chars in EF model config.
- Feed compatibility: supports both `transactionTime` and `timestamp` JSON names.

## Test coverage

- insert new transaction
- idempotent repeated runs
- update detection with field-level audit
- finalized record immutability
- revocation for missing in-window records

## Assumptions

- The feed represents the source-of-truth snapshot for the last 24 hours at run time.
- Timestamps are treated as UTC.
- Finalization is optional and controlled by config.

## Time tracking (for submission)

- Estimated hours: `<7>`
- Actual hours: `<6>`
- Repository link: `<https://github.com/Slade-roberts/Transactions-Ingest>`

## Possible future improvements

- Add EF migrations workflow (`dotnet ef`).
- Add pagination/streaming for larger snapshots.
- Add richer audit metadata (correlation ID, actor/source).
