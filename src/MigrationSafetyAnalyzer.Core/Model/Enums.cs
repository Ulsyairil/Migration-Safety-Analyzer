namespace MigrationSafetyAnalyzer.Core.Model;

/// <summary>Severity of a finding.</summary>
public enum Severity
{
    /// <summary>Informational hint, no risk on its own.</summary>
    Info = 0,

    /// <summary>Risk exists but is bounded or contextual.</summary>
    Warning = 1,

    /// <summary>Unsafe operation that can fail, lose data or block production.</summary>
    Error = 2,
}

/// <summary>Database impact class produced by provider profiling.</summary>
public enum LockLevel
{
    /// <summary>No lock impact.</summary>
    None = 0,

    /// <summary>Catalog-only change, no table scan.</summary>
    Metadata = 1,

    /// <summary>Short exclusive lock while catalog is updated.</summary>
    Brief = 2,

    /// <summary>Writes blocked for the whole operation.</summary>
    Blocking = 3,

    /// <summary>Reads and writes continue but operation takes a long time.</summary>
    Slow = 4,

    /// <summary>Whole table is copied under an exclusive lock.</summary>
    TableRewrite = 5,

    /// <summary>Existing data is removed.</summary>
    DataLoss = 6,
}

/// <summary>Supported database providers.</summary>
public enum ProviderKind
{
    /// <summary>Detect provider from the project configuration.</summary>
    Auto = 0,

    /// <summary>PostgreSQL.</summary>
    Postgres = 1,

    /// <summary>Microsoft SQL Server.</summary>
    SqlServer = 2,

    /// <summary>SQLite.</summary>
    Sqlite = 3,

    /// <summary>MySQL / MariaDB.</summary>
    MySql = 4,
}

/// <summary>Migration operations recognized by the analyzer.</summary>
public enum OperationKind
{
    /// <summary>Unrecognized operation.</summary>
    Unknown = 0,

    /// <summary>Adds a column.</summary>
    AddColumn,

    /// <summary>Alters an existing column.</summary>
    AlterColumn,

    /// <summary>Drops a column.</summary>
    DropColumn,

    /// <summary>Renames a column.</summary>
    RenameColumn,

    /// <summary>Renames a table.</summary>
    RenameTable,

    /// <summary>Adds a foreign key.</summary>
    AddForeignKey,

    /// <summary>Drops a foreign key.</summary>
    DropForeignKey,

    /// <summary>Adds a primary key.</summary>
    AddPrimaryKey,

    /// <summary>Drops a primary key.</summary>
    DropPrimaryKey,

    /// <summary>Adds an index.</summary>
    AddIndex,

    /// <summary>Drops an index.</summary>
    DropIndex,

    /// <summary>Adds a check constraint.</summary>
    AddCheckConstraint,

    /// <summary>Drops a check constraint.</summary>
    DropCheckConstraint,

    /// <summary>Creates a table.</summary>
    CreateTable,

    /// <summary>Drops a table.</summary>
    DropTable,

    /// <summary>Alters database level settings.</summary>
    AlterDatabase,

    /// <summary>Raw SQL statement.</summary>
    Sql,

    /// <summary>Seeds data.</summary>
    InsertData,

    /// <summary>Updates seeded data.</summary>
    UpdateData,

    /// <summary>Removes seeded data.</summary>
    DeleteData,
}
