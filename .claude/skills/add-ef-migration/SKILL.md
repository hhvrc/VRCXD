---
name: add-ef-migration
description: Change the server's Postgres model and add the EF Core migration for it, with the dotnet-ef command this repo uses and the checks CI runs. Use when adding or changing a table, column, index or Postgres enum in server/Common/VrcxDb.
---

# Add an EF Core migration

The model is `server/Common/VrcxDb/` (`Models/`, `VrcxContext.cs`, `Enums/`). Migrations are in
`server/Common/Migrations/`, made through the `MigrationHelper` startup project and the
`MigrationVrcxContext` context. The API applies them at startup; the test harness migrates a
template database once per run.

## 1. Change the model

- Entities in `Models/`, configuration in `VrcxContext.OnModelCreating`. Table and column names
  are explicit snake_case (`ToTable("worlds")`, `HasColumnName("stub_retry_at")`).
- Types: instants `timestamptz` (`DateTimeOffset`), arrays `text[]`, JSON `jsonb`, durations
  `interval`. A closed vocabulary of our own is a Postgres enum (`[PgEnum]`, registered through
  `NpgsqlEnumExtensions`); a VRChat vocabulary it can extend without notice stays `text`.
- Rows are the latest state; history goes in `*_changes` or event tables, written only when a
  value changed (`server/README.md`, Model). Momentary counts are not stored.
- Add an index for every new query shape that filters or orders a large table.

## 2. Add the migration

```sh
cd server
dotnet tool restore                      # dotnet-ef 11, pinned in .config/dotnet-tools.json
dotnet ef migrations add <PascalCaseName> --project Common --startup-project MigrationHelper \
    --context MigrationVrcxContext -o Migrations
```

- Adding never connects to a database. To undo the last one before committing, use
  `dotnet ef migrations remove` with the same `--project`, `--startup-project` and `--context`.
- Read the generated `Up` and `Down`. Fix the model and regenerate rather than editing the
  `.Designer.cs` or `MigrationVrcxContextModelSnapshot.cs` by hand. Data fixes (backfills) may be
  added to `Up` with `migrationBuilder.Sql(...)`.
- Keep the generated files as they are (class `_<timestamp>_<Name>`, CRLF).
- Never edit or delete a migration that is already on `develop` or `master`; add a new one.

## 3. Check

```sh
cd server
dotnet build VRCXBackend.slnx
dotnet ef migrations has-pending-model-changes --project Common --startup-project MigrationHelper \
    --context MigrationVrcxContext        # must say: No changes have been made to the model
dotnet test --project Common.Tests --treenode-filter "/*/*/StoreTests/*"
```

Then run the integration test classes for the feature. Tests that use the new column prove the
migration applies, since every scenario's database is cloned from the migrated template.

## 4. Docs and import

- Update the Model table in `server/README.md` for a new table.
- If the original VRCX had the data, check `server/LegacyImport/` and its mapping table in
  `server/LegacyImport/README.md`, and `LegacyImportTests`.
- Name the migration in the commit body ("the attempt stored (migration StubRefreshAttempts)").
