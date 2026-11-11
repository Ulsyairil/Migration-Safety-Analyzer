namespace MigrationSafetyAnalyzer.Core.Model;

/// <summary>A single safety issue found in a migration.</summary>
public sealed record Finding
{
    /// <summary>Stable rule identifier, for example <c>MSA003</c>.</summary>
    public required string RuleId { get; init; }

    /// <summary>Short rule title.</summary>
    public required string Title { get; init; }

    /// <summary>Severity of the finding.</summary>
    public required Severity Severity { get; init; }

    /// <summary>Human readable description of what is wrong.</summary>
    public required string Message { get; init; }

    /// <summary>Migration the finding belongs to.</summary>
    public required string MigrationId { get; init; }

    /// <summary>Source file of the migration.</summary>
    public string? FilePath { get; init; }

    /// <summary>Source line of the offending operation.</summary>
    public int? Line { get; init; }

    /// <summary>Database impact class.</summary>
    public LockLevel LockImpact { get; init; }

    /// <summary>Mechanism behind the impact.</summary>
    public string? Impact { get; init; }

    /// <summary>Suggested fix.</summary>
    public string? Remedy { get; init; }
}
