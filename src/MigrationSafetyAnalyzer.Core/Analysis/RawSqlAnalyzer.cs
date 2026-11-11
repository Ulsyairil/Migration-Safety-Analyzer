using System.Text.RegularExpressions;
using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Core.Analysis;

/// <summary>Flags dangerous patterns inside raw SQL passed to <c>MigrationBuilder.Sql</c>.</summary>
public static partial class RawSqlAnalyzer
{
    [GeneratedRegex(@"\bDROP\s+TABLE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DropTableRegex();

    [GeneratedRegex(@"\bTRUNCATE\b(\s+TABLE)?\s+(?:ONLY\s+)?[\""\[\]`a-zA-Z_][\w\.""\[\]`$]*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TruncateRegex();

    [GeneratedRegex(@"\bDELETE\s+FROM\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeleteRegex();

    [GeneratedRegex(@"\bUPDATE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UpdateRegex();

    [GeneratedRegex(@"\bWHERE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WhereRegex();

    [GeneratedRegex(@"\bCREATE\s+(UNIQUE\s+)?INDEX\b(?!\s+CONCURRENTLY)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateIndexRegex();

    [GeneratedRegex(@"\bCREATE\s+INDEX\s+CONCURRENTLY\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CreateIndexConcurrentlyRegex();

    [GeneratedRegex(@"\bLOCK\s+TABLE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LockTableRegex();

    [GeneratedRegex(@"\bACCESS\s+EXCLUSIVE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AccessExclusiveRegex();

    [GeneratedRegex(@"\bVACUUM\s+(FULL\b)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex VacuumRegex();

    [GeneratedRegex(@"\bSET\s+NOT\s+NULL\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SetNotNullRegex();

    [GeneratedRegex(@"\bALTER\s+COLUMN\b[\s\S]{0,80}?\bTYPE\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AlterColumnTypeRegex();

    /// <summary>Analyzes a raw SQL statement and returns findings.</summary>
    public static IReadOnlyList<Finding> Analyze(string sql, ProviderKind provider, string migrationId, string? filePath, int line)
    {
        var findings = new List<Finding>();
        var trimmed = sql.Trim();

        if (DropTableRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA020",
                Title = "Raw SQL drops a table",
                Severity = Severity.Error,
                Message = "Raw SQL contains DROP TABLE. All rows are removed and the operation cannot be rolled back by EF Core.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.Brief,
                Impact = "Permanent data loss for the dropped table.",
                Remedy = "Archive the table instead of dropping it, or use a reversible rename plus a background cleanup job.",
            });
        }

        if (TruncateRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA021",
                Title = "Raw SQL truncates a table",
                Severity = Severity.Error,
                Message = "Raw SQL contains TRUNCATE. Every row is removed without a rollback path.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.Blocking,
                Impact = "Permanent data loss; TRUNCATE also takes an exclusive lock for the duration.",
                Remedy = "Delete in bounded batches instead of truncating, or archive the rows first.",
            });
        }

        if (DeleteRegex().IsMatch(trimmed) && !WhereRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA022",
                Title = "Raw SQL deletes every row",
                Severity = Severity.Error,
                Message = "Raw SQL contains DELETE without a WHERE clause. This removes every row of the target table.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.Blocking,
                Impact = "Unbounded delete locks rows and removes all data.",
                Remedy = "Add an explicit WHERE clause and delete in batches with a bounded LIMIT.",
            });
        }

        if (UpdateRegex().IsMatch(trimmed) && !WhereRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA023",
                Title = "Raw SQL updates every row",
                Severity = Severity.Error,
                Message = "Raw SQL contains UPDATE without a WHERE clause. Every row is rewritten.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.Blocking,
                Impact = "Unbounded update rewrites the whole table and can trigger cascades.",
                Remedy = "Add an explicit WHERE clause and update in batches.",
            });
        }

        if (provider == ProviderKind.Postgres && CreateIndexRegex().IsMatch(trimmed)
            && !CreateIndexConcurrentlyRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA024",
                Title = "Index built without CONCURRENTLY",
                Severity = Severity.Warning,
                Message = "CREATE INDEX without CONCURRENTLY blocks writes on the table for the whole build.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.Slow,
                Impact = "Write downtime proportional to table size.",
                Remedy = "Use CREATE INDEX CONCURRENTLY, or build the index outside the migration and drop the index operation from the migration.",
            });
        }

        if (LockTableRegex().IsMatch(trimmed) && AccessExclusiveRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA025",
                Title = "Raw SQL takes an ACCESS EXCLUSIVE lock",
                Severity = Severity.Error,
                Message = "Raw SQL requests ACCESS EXCLUSIVE. Every concurrent read and write on the table is blocked.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.Blocking,
                Impact = "Full table outage for the duration of the statement.",
                Remedy = "Keep the lock window short, or split the migration so the exclusive section runs alone.",
            });
        }

        if (VacuumRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA026",
                Title = "VACUUM rewrites the table",
                Severity = Severity.Warning,
                Message = "VACUUM FULL rewrites the entire table and holds an exclusive lock.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.TableRewrite,
                Impact = "Table rewrite with exclusive locking.",
                Remedy = "Use plain VACUUM or schedule VACUUM FULL as a separate maintenance operation.",
            });
        }

        if (SetNotNullRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA027",
                Title = "Raw SQL sets NOT NULL",
                Severity = Severity.Warning,
                Message = "Raw SQL applies SET NOT NULL. The full table is validated and any NULL row aborts the migration.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.Blocking,
                Impact = "Full table scan under lock; migration fails if NULL values exist.",
                Remedy = "Backfill NULLs in batches first, then apply the constraint.",
            });
        }

        if (AlterColumnTypeRegex().IsMatch(trimmed))
        {
            findings.Add(new Finding
            {
                RuleId = "MSA028",
                Title = "Raw SQL changes a column type",
                Severity = Severity.Warning,
                Message = "Raw SQL alters a column type. The table is rewritten under an exclusive lock.",
                MigrationId = migrationId,
                FilePath = filePath,
                Line = line,
                LockImpact = LockLevel.TableRewrite,
                Impact = "Table rewrite under exclusive lock.",
                Remedy = "Add the new column, backfill, then drop the old one in a later migration.",
            });
        }

        return findings;
    }
}
