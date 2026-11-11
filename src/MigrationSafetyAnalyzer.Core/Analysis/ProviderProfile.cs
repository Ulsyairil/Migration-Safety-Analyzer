using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Core.Analysis;

/// <summary>Describes how a single operation behaves on a given database.</summary>
/// <param name="Lock">Lock impact of the operation.</param>
/// <param name="RewritesTable">Whether the operation copies the whole table.</param>
/// <param name="LocksTable">Whether writes are blocked while the operation runs.</param>
/// <param name="Reason">Why the operation behaves this way.</param>
public sealed record OperationImpact(LockLevel Lock, bool RewritesTable, bool LocksTable, string Reason);

/// <summary>Provider-specific semantics used to grade operations.</summary>
public interface IProviderProfile
{
    /// <summary>Provider identifier.</summary>
    ProviderKind Kind { get; }

    /// <summary>Human readable provider name.</summary>
    string DisplayName { get; }

    /// <summary>Describes the impact of an operation on this provider.</summary>
    OperationImpact Describe(MigrationOperation op);

    /// <summary>Whether adding a non-nullable column without a default requires a table rewrite.</summary>
    bool NonNullableAddRewrites { get; }

    /// <summary>Whether adding a foreign key validates existing rows while holding a lock.</summary>
    bool ForeignKeyValidatesExistingRows { get; }

    /// <summary>Whether a unique constraint is enforced with an index rebuild.</summary>
    bool UniqueConstraintRebuilds { get; }
}

/// <summary>PostgreSQL profile.</summary>
public sealed class PostgresProfile : IProviderProfile
{
    /// <inheritdoc />
    public ProviderKind Kind => ProviderKind.Postgres;

    /// <inheritdoc />
    public string DisplayName => "PostgreSQL";

    /// <inheritdoc />
    public bool NonNullableAddRewrites => false;

    /// <inheritdoc />
    public bool ForeignKeyValidatesExistingRows => true;

    /// <inheritdoc />
    public bool UniqueConstraintRebuilds => true;

    /// <inheritdoc />
    public OperationImpact Describe(MigrationOperation op) => op.Kind switch
    {
        OperationKind.AddColumn when op.Nullable == true =>
            new OperationImpact(LockLevel.Brief, false, false,
                "Nullable column: metadata-only, no table rewrite in PostgreSQL 11+."),
        OperationKind.AddColumn when op.HasDefault && !op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.Brief, false, false,
                "Constant default: PostgreSQL 11+ stores the default in the catalog without rewriting the table."),
        OperationKind.AddColumn when op.HasDefault && op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Volatile default: PostgreSQL rewrites every existing row to materialize the value."),
        OperationKind.AddColumn =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "NOT NULL column without default: every existing row must be rewritten under ACCESS EXCLUSIVE."),
        OperationKind.AlterColumn when op.TypeChanged =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Type change rewrites the table and holds ACCESS EXCLUSIVE for the whole copy."),
        OperationKind.AlterColumn when op.Nullable == false && op.OldNullable == true =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "NOT NULL validation scans the table and fails if any NULL exists."),
        OperationKind.DropColumn =>
            new OperationImpact(LockLevel.Brief, false, false,
                "Dropping a column is metadata-only, but the data is lost immediately."),
        OperationKind.AddForeignKey =>
            new OperationImpact(LockLevel.Brief, false, true,
                "Foreign key validation scans the referenced table and holds SHARE ROW EXCLUSIVE on both tables."),
        OperationKind.AddIndex =>
            new OperationImpact(LockLevel.Slow, false, false,
                "Plain CREATE INDEX blocks writes on the table until the build completes."),
        OperationKind.AddPrimaryKey =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Primary key creation builds a unique index and holds ACCESS EXCLUSIVE."),
        OperationKind.CreateTable =>
            new OperationImpact(LockLevel.None, false, false, "New empty table, no impact on existing data."),
        OperationKind.DropTable =>
            new OperationImpact(LockLevel.Brief, false, false, "Table dropped, all data removed."),
        _ => new OperationImpact(LockLevel.Metadata, false, false, "Metadata-only change."),
    };
}

/// <summary>SQL Server profile.</summary>
public sealed class SqlServerProfile : IProviderProfile
{
    /// <inheritdoc />
    public ProviderKind Kind => ProviderKind.SqlServer;

    /// <inheritdoc />
    public string DisplayName => "SQL Server";

    /// <inheritdoc />
    public bool NonNullableAddRewrites => true;

    /// <inheritdoc />
    public bool ForeignKeyValidatesExistingRows => true;

    /// <inheritdoc />
    public bool UniqueConstraintRebuilds => true;

    /// <inheritdoc />
    public OperationImpact Describe(MigrationOperation op) => op.Kind switch
    {
        OperationKind.AddColumn when op.Nullable == true =>
            new OperationImpact(LockLevel.Brief, false, false,
                "Nullable column: metadata-only unless a default constraint is added WITH VALUES."),
        OperationKind.AddColumn when op.HasDefault && !op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "Default constraint is applied to existing rows, which locks the table for the duration."),
        OperationKind.AddColumn when op.HasDefault && op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "Volatile default is evaluated per existing row while the table is locked."),
        OperationKind.AddColumn =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "NOT NULL column without default fails on non-empty tables and locks the table."),
        OperationKind.AlterColumn when op.TypeChanged =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "Type change rewrites the table and holds a schema modification lock."),
        OperationKind.AlterColumn when op.Nullable == false && op.OldNullable == true =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "NOT NULL validation rewrites the table when the column contains NULLs."),
        OperationKind.DropColumn =>
            new OperationImpact(LockLevel.Brief, false, false,
                "Dropping a column is metadata-only, but the data is lost immediately."),
        OperationKind.AddForeignKey =>
            new OperationImpact(LockLevel.Brief, false, true,
                "Foreign key validation scans the table and holds a schema modification lock."),
        OperationKind.AddIndex =>
            new OperationImpact(LockLevel.Slow, false, false,
                "Plain CREATE INDEX blocks writes for the duration of the build."),
        OperationKind.AddPrimaryKey =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Primary key creation builds a clustered index and holds a schema modification lock."),
        OperationKind.CreateTable =>
            new OperationImpact(LockLevel.None, false, false, "New empty table, no impact on existing data."),
        OperationKind.DropTable =>
            new OperationImpact(LockLevel.Brief, false, false, "Table dropped, all data removed."),
        _ => new OperationImpact(LockLevel.Metadata, false, false, "Metadata-only change."),
    };
}

/// <summary>SQLite profile.</summary>
public sealed class SqliteProfile : IProviderProfile
{
    /// <inheritdoc />
    public ProviderKind Kind => ProviderKind.Sqlite;

    /// <inheritdoc />
    public string DisplayName => "SQLite";

    /// <inheritdoc />
    public bool NonNullableAddRewrites => false;

    /// <inheritdoc />
    public bool ForeignKeyValidatesExistingRows => false;

    /// <inheritdoc />
    public bool UniqueConstraintRebuilds => false;

    /// <inheritdoc />
    public OperationImpact Describe(MigrationOperation op) => op.Kind switch
    {
        OperationKind.AddColumn when op.Nullable == true =>
            new OperationImpact(LockLevel.Brief, false, false,
                "SQLite supports ADD COLUMN with a NULL default, no rewrite."),
        OperationKind.AddColumn when op.HasDefault && !op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.Brief, false, false,
                "Constant default is stored in the schema, no rewrite."),
        OperationKind.AddColumn when op.HasDefault && op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Non-constant default forces the legacy add-column procedure, which rewrites the table."),
        OperationKind.AddColumn =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "NOT NULL column without default cannot be added to a non-empty table without a rewrite."),
        OperationKind.AlterColumn when op.TypeChanged =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "SQLite has no ALTER COLUMN: EF Core rebuilds the table and copies every row."),
        OperationKind.AlterColumn when op.Nullable == false && op.OldNullable == true =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "SQLite cannot tighten nullability in place, the table is rebuilt."),
        OperationKind.DropColumn =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "SQLite has no DROP COLUMN before 3.35 and EF Core rebuilds the table regardless."),
        OperationKind.AddForeignKey =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Foreign keys are part of the table definition in SQLite, so the table is rebuilt."),
        OperationKind.AddIndex =>
            new OperationImpact(LockLevel.Brief, false, false, "Indexes are separate objects in SQLite."),
        OperationKind.AddPrimaryKey =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Primary keys are part of the table definition in SQLite, so the table is rebuilt."),
        OperationKind.CreateTable =>
            new OperationImpact(LockLevel.None, false, false, "New empty table, no impact on existing data."),
        OperationKind.DropTable =>
            new OperationImpact(LockLevel.Brief, false, false, "Table dropped, all data removed."),
        _ => new OperationImpact(LockLevel.Metadata, false, false, "Metadata-only change."),
    };
}

/// <summary>MySQL / MariaDB profile.</summary>
public sealed class MySqlProfile : IProviderProfile
{
    /// <inheritdoc />
    public ProviderKind Kind => ProviderKind.MySql;

    /// <inheritdoc />
    public string DisplayName => "MySQL / MariaDB";

    /// <inheritdoc />
    public bool NonNullableAddRewrites => true;

    /// <inheritdoc />
    public bool ForeignKeyValidatesExistingRows => true;

    /// <inheritdoc />
    public bool UniqueConstraintRebuilds => false;

    /// <inheritdoc />
    public OperationImpact Describe(MigrationOperation op) => op.Kind switch
    {
        OperationKind.AddColumn when op.Nullable == true =>
            new OperationImpact(LockLevel.Brief, false, false,
                "Nullable column: INSTANT algorithm in MySQL 8.0.12+."),
        OperationKind.AddColumn when op.HasDefault && !op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "Default is applied to existing rows, which locks the table for the duration."),
        OperationKind.AddColumn when op.HasDefault && op.DefaultIsVolatile =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "Volatile default is evaluated per existing row while the table is locked."),
        OperationKind.AddColumn =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "NOT NULL column without default fails on non-empty tables and locks the table."),
        OperationKind.AlterColumn when op.TypeChanged =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Type change rebuilds the table under an exclusive lock in most cases."),
        OperationKind.AlterColumn when op.Nullable == false && op.OldNullable == true =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "Tightening nullability rewrites the table when NULLs exist."),
        OperationKind.DropColumn =>
            new OperationImpact(LockLevel.Blocking, true, true,
                "Dropping a column rebuilds the table in MySQL 8 when INSTANT is not supported."),
        OperationKind.AddForeignKey =>
            new OperationImpact(LockLevel.Brief, false, true,
                "Foreign key validation takes a shared lock and scans the table."),
        OperationKind.AddIndex =>
            new OperationImpact(LockLevel.Slow, false, false,
                "Plain CREATE INDEX blocks writes for the duration of the build."),
        OperationKind.AddPrimaryKey =>
            new OperationImpact(LockLevel.TableRewrite, true, true,
                "Primary key creation rebuilds the table under an exclusive lock."),
        OperationKind.CreateTable =>
            new OperationImpact(LockLevel.None, false, false, "New empty table, no impact on existing data."),
        OperationKind.DropTable =>
            new OperationImpact(LockLevel.Brief, false, false, "Table dropped, all data removed."),
        _ => new OperationImpact(LockLevel.Metadata, false, false, "Metadata-only change."),
    };
}

/// <summary>Resolves provider profiles.</summary>
public static class ProviderProfiles
{
    /// <summary>Returns the profile for a provider kind.</summary>
    public static IProviderProfile For(ProviderKind kind) => kind switch
    {
        ProviderKind.Postgres => new PostgresProfile(),
        ProviderKind.SqlServer => new SqlServerProfile(),
        ProviderKind.Sqlite => new SqliteProfile(),
        ProviderKind.MySql => new MySqlProfile(),
        _ => new PostgresProfile(),
    };
}
