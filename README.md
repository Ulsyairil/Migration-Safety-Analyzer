# Migration Safety Analyzer

Static safety analyzer for Entity Framework Core migrations. Grades lock impact, table rewrites
and data loss **per database provider**, so the same migration is judged differently on
PostgreSQL than on SQL Server.

No EF Core dependency. The analyzer parses migration source with Roslyn, so it runs in CI
without a database, without a `DbContext`, and without restoring the target project.

## Why

`DropColumn` followed by `AddColumn` on the same column looks reasonable in a diff, compiles
without warnings and passes CI. Then it runs against production and every value in that column
is gone.

The same migration is safe on one database and destructive on another:

| Operation | PostgreSQL | SQL Server | SQLite | MySQL |
|---|---|---|---|---|
| Add nullable column | metadata only | metadata only | metadata only | metadata only |
| Add NOT NULL, no default | table rewrite | fails on non-empty table | table rewrite | fails on non-empty table |
| Add column, constant default | metadata only (PG 11+) | locks table | metadata only | locks table |
| Add column, volatile default | table rewrite | locks table | table rewrite | locks table |
| Change column type | table rewrite | locks table | table rebuild | table rebuild |
| Add foreign key | validates existing rows | validates existing rows | table rebuild | validates existing rows |
| Add primary key | builds unique index | builds clustered index | table rebuild | table rebuild |

This tool knows that table.

## Install

```bash
dotnet tool install --global ulsyairil.MigrationSafetyAnalyzer.Cli
```

Or add it to a repository:

```bash
dotnet new tool-manifest
dotnet tool install ulsyairil.MigrationSafetyAnalyzer.Cli
```

## Usage

```bash
# Analyze the current directory, provider detected from package references
migration-safety

# Explicit provider
migration-safety --provider postgres

# CI gate, JSON output
migration-safety --path ./src/MyApp --json --fail-on error > report.json
```

### Options

| Option | Default | Meaning |
|---|---|---|
| `--path`, `-p` | current directory | Directory that contains the project and its migrations |
| `--provider` | `auto` | `auto`, `postgres`, `sqlserver`, `sqlite`, `mysql` |
| `--json` | off | Emit a JSON report for CI |
| `--fail-on` | `error` | Exit code 1 when a finding of this severity or higher exists: `error`, `warning`, `info`, `none` |
| `--min-severity` | `info` | Hide findings below this severity |
| `--up` | on | Analyze only the `Up` path |
| `--down` | off | Analyze only the `Down` path |
| `--both` | off | Analyze both paths |

Exit codes: `0` clean, `1` findings at or above `--fail-on`, `2` bad arguments.

### Provider detection

`--provider auto` reads the project files and picks the first match:

- `Npgsql.EntityFrameworkCore.PostgreSQL` → PostgreSQL
- `Microsoft.EntityFrameworkCore.SqlServer` → SQL Server
- `Microsoft.EntityFrameworkCore.Sqlite` → SQLite
- `Pomelo.EntityFrameworkCore.MySql`, `MySql.EntityFrameworkCore`, `MySqlConnector` → MySQL

When no marker is found the tool falls back to PostgreSQL and says so in the report.

## Rules

| Rule | Severity | What it finds |
|---|---|---|
| `MSA001` | error | Column dropped and re-added in the same migration. Silent data loss |
| `MSA002` | error | NOT NULL column added without a default on a populated table |
| `MSA003` | warning | Volatile default (`now()`, `newid()`, `getdate()`) forces a table rewrite |
| `MSA004` | error | Column, table or seed rows dropped |
| `MSA005` | warning | Column type changed, which rewrites the table |
| `MSA006` | warning | Nullability tightened to NOT NULL without a proven backfill |
| `MSA007` | warning | Foreign key added to a populated table, which validates existing rows |
| `MSA008` | warning | Primary key added to a populated table |
| `MSA009` | warning | Migration has no `Down`, or the `Down` does not restore what `Up` dropped |
| `MSA010` | warning | Unique column added to a populated table |
| `MSA011` | warning | Index build blocks writes |
| `MSA012` | — | Raw SQL, delegated to the rules below |
| `MSA020` | error | Raw SQL drops a table |
| `MSA021` | error | Raw SQL truncates a table |
| `MSA022` | error | Raw SQL deletes without a `WHERE` clause |
| `MSA023` | error | Raw SQL updates without a `WHERE` clause |
| `MSA024` | warning | `CREATE INDEX` without `CONCURRENTLY` on PostgreSQL |
| `MSA025` | error | Raw SQL takes an `ACCESS EXCLUSIVE` lock |
| `MSA026` | warning | `VACUUM FULL` rewrites the table |
| `MSA027` | warning | Raw SQL applies `SET NOT NULL` |
| `MSA028` | warning | Raw SQL alters a column type |

## Example

```
$ migration-safety --path samples/SampleApp
migration-safety — SampleApp
provider: PostgreSQL (detected)
files scanned: 4   migrations analyzed: 4

── 20240215093000 ──
  ERROR MSA002  NOT NULL column added without a default
         Column 'Invoices.Status' is added as NOT NULL without a default value.
         impact: NOT NULL column without default: every existing row must be rewritten under ACCESS EXCLUSIVE.
         remedy: Add the column as nullable first, backfill existing rows in batches, then tighten it in a later migration.
         lock: TableRewrite   line 10

errors: 4   warnings: 4   info: 0
```

## Use as a library

```csharp
using MigrationSafetyAnalyzer.Core;
using MigrationSafetyAnalyzer.Core.Model;

var analyzer = new MigrationSafetyAnalyzer.Core.MigrationSafetyAnalyzer();
var report = analyzer.AnalyzeDirectory(
    "./src/MyApp",
    new AnalysisOptions { Provider = ProviderKind.Postgres });

foreach (var finding in report.Findings)
{
    Console.WriteLine($"{finding.RuleId} {finding.Severity} {finding.MigrationId}: {finding.Message}");
}
```

## CI

```yaml
- name: Migration safety
  run: |
    dotnet tool restore
    dotnet migration-safety --path ./src/MyApp --json --fail-on error > migration-safety.json
```

## Scope

What the analyzer reads: migration source files (`Migrations/` folders and timestamp-prefixed
file names). It does not load the EF Core model, does not need a database connection, and does
not run migrations.

What it does not do: estimate lock duration in seconds, because that needs live row counts.
It reports the mechanism and the lock class instead, so the answer stays factual.

## License

MIT
