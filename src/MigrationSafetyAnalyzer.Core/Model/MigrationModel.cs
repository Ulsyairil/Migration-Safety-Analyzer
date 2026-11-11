namespace MigrationSafetyAnalyzer.Core.Model;

/// <summary>A parsed EF Core migration.</summary>
public sealed record MigrationModel
{
    /// <summary>Migration identifier, usually <c>20241103120000_Name</c>.</summary>
    public required string Id { get; init; }

    /// <summary>Migration class name.</summary>
    public required string ClassName { get; init; }

    /// <summary>Absolute path of the migration source file.</summary>
    public required string FilePath { get; init; }

    /// <summary>Operations declared in <c>Up</c>.</summary>
    public IReadOnlyList<MigrationOperation> Up { get; init; } = [];

    /// <summary>Operations declared in <c>Down</c>.</summary>
    public IReadOnlyList<MigrationOperation> Down { get; init; } = [];

    /// <summary>Whether a <c>Down</c> override exists.</summary>
    public bool HasDown { get; init; }
}
