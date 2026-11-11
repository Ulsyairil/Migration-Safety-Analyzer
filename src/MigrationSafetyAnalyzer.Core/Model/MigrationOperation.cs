namespace MigrationSafetyAnalyzer.Core.Model;

/// <summary>A single operation extracted from an EF Core migration source file.</summary>
public sealed record MigrationOperation
{
    /// <summary>Normalized operation kind.</summary>
    public required OperationKind Kind { get; init; }

    /// <summary>Original method name on <c>MigrationBuilder</c>.</summary>
    public string MethodName { get; init; } = string.Empty;

    /// <summary>Either <c>Up</c> or <c>Down</c>.</summary>
    public string Direction { get; init; } = "Up";

    /// <summary>Target table, when known.</summary>
    public string? Table { get; init; }

    /// <summary>Target column, when known.</summary>
    public string? Column { get; init; }

    /// <summary>New name for a rename operation.</summary>
    public string? NewName { get; init; }

    /// <summary>Nullability of the resulting column, when known.</summary>
    public bool? Nullable { get; init; }

    /// <summary>Nullability before the operation, when known.</summary>
    public bool? OldNullable { get; init; }

    /// <summary>Whether the column is unique, when known.</summary>
    public bool? Unique { get; init; }

    /// <summary>Whether a default value is present.</summary>
    public bool HasDefault { get; init; }

    /// <summary>Literal default value or default SQL expression.</summary>
    public string? DefaultValue { get; init; }

    /// <summary>Whether the default expression is evaluated per row.</summary>
    public bool DefaultIsVolatile { get; init; }

    /// <summary>Store type of the resulting column, when known.</summary>
    public string? ColumnType { get; init; }

    /// <summary>Store type before the operation, when known.</summary>
    public string? OldColumnType { get; init; }

    /// <summary>Whether the column type changed.</summary>
    public bool TypeChanged { get; init; }

    /// <summary>Whether the type change could be verified from the source.</summary>
    public bool TypeChangeVerified { get; init; }

    /// <summary>Referenced table for a foreign key.</summary>
    public string? PrincipalTable { get; init; }

    /// <summary>Referenced column for a foreign key.</summary>
    public string? PrincipalColumn { get; init; }

    /// <summary>Raw SQL text for <see cref="OperationKind.Sql"/>.</summary>
    public string? RawSql { get; init; }

    /// <summary>Column belongs to a table created in the same migration.</summary>
    public bool IsNewTableColumn { get; init; }

    /// <summary>Columns declared by a CreateTable operation.</summary>
    public IReadOnlyList<string> CreatedColumns { get; init; } = [];

    /// <summary>Source line of the operation.</summary>
    public int Line { get; init; }
}
