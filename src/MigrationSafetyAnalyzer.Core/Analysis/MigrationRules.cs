using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Core.Analysis;

/// <summary>Context passed to every rule evaluation.</summary>
public sealed record RuleContext
{
    /// <summary>The migration being evaluated.</summary>
    public required MigrationModel Migration { get; init; }

    /// <summary>The operation being evaluated.</summary>
    public required MigrationOperation Operation { get; init; }

    /// <summary>Provider profile.</summary>
    public required IProviderProfile Profile { get; init; }

    /// <summary>Schema state before the current operation.</summary>
    public required SchemaTracker Schema { get; init; }

    /// <summary>Operations of the same migration, in order.</summary>
    public required IReadOnlyList<MigrationOperation> Siblings { get; init; }

    /// <summary>Whether the current operation belongs to the Down path.</summary>
    public bool IsDown => string.Equals(Operation.Direction, "Down", StringComparison.Ordinal);

    /// <summary>Whether the current operation is the first one of the migration.</summary>
    public bool IsFirstOperation { get; init; }
}

/// <summary>Grades a single operation against provider semantics.</summary>
public interface IMigrationRule
{
    /// <summary>Rule identifier.</summary>
    string RuleId { get; }

    /// <summary>Evaluates one operation.</summary>
    IEnumerable<Finding> Evaluate(RuleContext context);
}

/// <summary>Base class with shared helpers for building findings.</summary>
public abstract class MigrationRule : IMigrationRule
{
    /// <inheritdoc />
    public abstract string RuleId { get; }

    /// <inheritdoc />
    public abstract IEnumerable<Finding> Evaluate(RuleContext context);

    /// <summary>Creates a finding bound to the current migration and operation.</summary>
    protected Finding Finding(
        RuleContext context,
        string title,
        Severity severity,
        string message,
        LockLevel lockImpact,
        string? impact,
        string? remedy)
    {
        var op = context.Operation;
        return new Finding
        {
            RuleId = RuleId,
            Title = title,
            Severity = severity,
            Message = message,
            MigrationId = context.Migration.Id,
            FilePath = context.Migration.FilePath,
            Line = op.Line,
            LockImpact = lockImpact,
            Impact = impact,
            Remedy = remedy,
        };
    }
}

/// <summary>Finds columns dropped and re-added, which silently destroys data.</summary>
public sealed class DropAndReAddRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA001";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AddColumn || op.Table is null || op.Column is null)
        {
            yield break;
        }

        // A rollback re-adds a column on purpose, so the pattern only matters on the Up path.
        if (context.IsDown)
        {
            yield break;
        }

        if (!op.IsNewTableColumn && context.Schema.DroppedColumns.TryGetValue(op.Table, out var dropped)
            && dropped.Contains(op.Column))
        {
            yield return Finding(
                context,
                "Column dropped and re-added in the same migration",
                Severity.Error,
                $"Column '{op.Table}.{op.Column}' is dropped and then re-added. The re-added column starts empty, so all existing values are lost without an error.",
                LockLevel.DataLoss,
                "Silent data loss: the column reappears with default or NULL values.",
                "Use RenameColumn to preserve the data, or split the migration into drop-then-add so the value loss is explicit.");
        }
    }
}

/// <summary>Finds NOT NULL columns added without a default on non-empty tables.</summary>
public sealed class NonNullableAddColumnRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA002";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AddColumn || op.Table is null || op.Column is null)
        {
            yield break;
        }

        if (op.Nullable != false || op.HasDefault || op.IsNewTableColumn)
        {
            yield break;
        }

        if (!context.Schema.HasTable(op.Table))
        {
            yield break;
        }

        var impact = context.Profile.Describe(op);

        yield return Finding(
            context,
            "NOT NULL column added without a default",
            Severity.Error,
            $"Column '{op.Table}.{op.Column}' is added as NOT NULL without a default value.",
            impact.Lock,
            impact.Reason,
            "Add the column as nullable first, backfill existing rows in batches, then tighten it in a later migration.");
    }
}

/// <summary>Finds volatile defaults that force a table rewrite.</summary>
public sealed class VolatileDefaultRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA003";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AddColumn || !op.HasDefault || !op.DefaultIsVolatile)
        {
            yield break;
        }

        var impact = context.Profile.Describe(op);

        yield return Finding(
            context,
            "Volatile default forces a table rewrite",
            Severity.Warning,
            $"Column '{op.Table}.{op.Column}' uses a volatile default ({op.DefaultValue}). The default is evaluated per existing row.",
            impact.Lock,
            impact.Reason,
            "Add the column as nullable, backfill in batches, then set the default in a separate migration.");
    }
}

/// <summary>Finds dropped columns and tables.</summary>
public sealed class DataLossRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA004";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        var impact = context.Profile.Describe(op);

        // A rollback is destructive by design, so data loss on the Down path is not a finding.
        if (context.IsDown)
        {
            yield break;
        }

        if (op.Kind == OperationKind.DropColumn && op.Table is not null && op.Column is not null)
        {
            yield return Finding(
                context,
                "Column dropped",
                Severity.Error,
                $"Column '{op.Table}.{op.Column}' is dropped. Every stored value is deleted.",
                LockLevel.DataLoss,
                "Permanent data loss. The down migration restores the column but not the values.",
                "Rename the column instead of dropping it, and remove it in a later release once no code reads it.");

            yield break;
        }

        if (op.Kind == OperationKind.DropTable && op.Table is not null)
        {
            yield return Finding(
                context,
                "Table dropped",
                Severity.Error,
                $"Table '{op.Table}' is dropped. Every row is deleted.",
                LockLevel.DataLoss,
                "Permanent data loss for the whole table.",
                "Drop the table only after it is empty and no code references it; otherwise archive it first.");

            yield break;
        }

        if (op.Kind == OperationKind.DeleteData)
        {
            yield return Finding(
                context,
                "Seeded rows deleted",
                Severity.Warning,
                "Seeded rows are removed by the migration.",
                impact.Lock,
                "Seed data is removed. Seed data is normally re-applied by the next migration.",
                null);
        }
    }
}

/// <summary>Finds type changes that rewrite the table.</summary>
public sealed class TypeChangeRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA005";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AlterColumn || !op.TypeChanged || !op.TypeChangeVerified)
        {
            yield break;
        }

        var impact = context.Profile.Describe(op);

        yield return Finding(
            context,
            "Column type changed",
            Severity.Warning,
            $"Column '{op.Table}.{op.Column}' changes type from '{op.OldColumnType}' to '{op.ColumnType}'. The table is rewritten.",
            impact.Lock,
            impact.Reason,
            "Add the new column, backfill it in batches, switch the application to read the new column, then drop the old one.");
    }
}

/// <summary>Finds nullability tightened without a proven backfill.</summary>
public sealed class TightenNullabilityRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA006";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AlterColumn || op.Nullable != false || op.OldNullable != true)
        {
            yield break;
        }

        var impact = context.Profile.Describe(op);

        yield return Finding(
            context,
            "Column tightened to NOT NULL",
            Severity.Warning,
            $"Column '{op.Table}.{op.Column}' changes from nullable to NOT NULL. Every existing row is validated.",
            impact.Lock,
            impact.Reason,
            "Backfill NULL values in bounded batches in an earlier migration, then apply the NOT NULL constraint.");
    }
}

/// <summary>Finds foreign keys added to tables that already contain data.</summary>
public sealed class ForeignKeyValidationRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA007";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AddForeignKey || !context.Profile.ForeignKeyValidatesExistingRows)
        {
            yield break;
        }

        if (op.Table is null || !context.Schema.HasTable(op.Table))
        {
            yield break;
        }

        var impact = context.Profile.Describe(op);

        yield return Finding(
            context,
            "Foreign key validates existing rows",
            Severity.Warning,
            $"Foreign key on '{op.Table}' references '{op.PrincipalTable}'. Existing rows are checked against the parent table while a lock is held.",
            impact.Lock,
            impact.Reason,
            "Deduplicate and repair orphaned rows before adding the constraint, then add it in a dedicated migration.");
    }
}

/// <summary>Finds primary keys added to populated tables.</summary>
public sealed class PrimaryKeyRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA008";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AddPrimaryKey || op.Table is null)
        {
            yield break;
        }

        if (!context.Schema.HasTable(op.Table))
        {
            yield break;
        }

        var impact = context.Profile.Describe(op);

        yield return Finding(
            context,
            "Primary key added to a populated table",
            Severity.Warning,
            $"Primary key is added to '{op.Table}'. A unique index is built and duplicates cause the migration to fail.",
            impact.Lock,
            impact.Reason,
            "Deduplicate rows before adding the primary key, and add it in a dedicated migration.");
    }
}

/// <summary>Finds missing or asymmetric down migrations.</summary>
public sealed class DownMigrationRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA009";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var migration = context.Migration;
        if (context.IsDown || !migration.Up.Any())
        {
            yield break;
        }

        // This rule is about the migration as a whole, so it only runs once per migration.
        if (!context.IsFirstOperation)
        {
            yield break;
        }

        if (!migration.HasDown)
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Title = "Migration has no down path",
                Severity = Severity.Warning,
                Message = $"Migration '{migration.Id}' does not override Down. The schema cannot be rolled back through EF Core.",
                MigrationId = migration.Id,
                FilePath = migration.FilePath,
                LockImpact = LockLevel.None,
                Impact = "A failed deployment cannot be reverted with dotnet ef database update <previous>.",
                Remedy = "Implement Down() so the migration is reversible.",
            };

            yield break;
        }

        var upKinds = migration.Up.Select(o => o.Kind).ToHashSet();
        var downKinds = migration.Down.Select(o => o.Kind).ToHashSet();

        if (upKinds.Contains(OperationKind.DropColumn) && !downKinds.Contains(OperationKind.AddColumn))
        {
            yield return new Finding
            {
                RuleId = RuleId,
                Title = "Down migration does not restore the dropped column",
                Severity = Severity.Warning,
                Message = $"Migration '{migration.Id}' drops a column but the down path does not add it back.",
                MigrationId = migration.Id,
                FilePath = migration.FilePath,
                LockImpact = LockLevel.None,
                Impact = "Rolling back leaves the schema different from the previous release.",
                Remedy = "Add the column back in Down().",
            };
        }
    }
}

/// <summary>Finds unique constraints added to existing columns.</summary>
public sealed class UniqueConstraintRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA010";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AddColumn || op.Unique != true || op.Table is null || op.Column is null)
        {
            yield break;
        }

        if (!context.Schema.HasTable(op.Table))
        {
            yield break;
        }

        yield return Finding(
            context,
            "Unique column added to a populated table",
            Severity.Warning,
            $"Column '{op.Table}.{op.Column}' is added with a unique constraint. Existing rows are checked and duplicates fail the migration.",
            LockLevel.Blocking,
            "Unique index build plus a full duplicate scan under a lock.",
            "Create the unique index separately, after deduplicating existing rows.");
    }
}

/// <summary>Finds indexes built on populated tables without the concurrent option.</summary>
public sealed class IndexBuildRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA011";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.AddIndex || op.Table is null)
        {
            yield break;
        }

        if (!context.Schema.HasTable(op.Table))
        {
            yield break;
        }

        var impact = context.Profile.Describe(op);
        if (impact.Lock < LockLevel.Slow)
        {
            yield break;
        }

        var remedy = context.Profile.Kind == ProviderKind.Postgres
            ? "Build the index with CREATE INDEX CONCURRENTLY, or create it outside the migration and drop the index operation from the migration."
            : "Build the index in a separate migration so the write-blocking window is isolated.";

        yield return Finding(
            context,
            "Index build blocks writes",
            Severity.Warning,
            $"An index is added to '{op.Table}'. Writes are blocked while the index is built.",
            impact.Lock,
            impact.Reason,
            remedy);
    }
}

/// <summary>Finds raw SQL statements and delegates to the raw SQL analyzer.</summary>
public sealed class RawSqlRule : MigrationRule
{
    /// <inheritdoc />
    public override string RuleId => "MSA012";

    /// <inheritdoc />
    public override IEnumerable<Finding> Evaluate(RuleContext context)
    {
        var op = context.Operation;
        if (op.Kind != OperationKind.Sql || string.IsNullOrWhiteSpace(op.RawSql))
        {
            yield break;
        }

        var findings = RawSqlAnalyzer.Analyze(
            op.RawSql,
            context.Profile.Kind,
            context.Migration.Id,
            context.Migration.FilePath,
            op.Line);

        foreach (var finding in findings)
        {
            yield return finding;
        }
    }
}
