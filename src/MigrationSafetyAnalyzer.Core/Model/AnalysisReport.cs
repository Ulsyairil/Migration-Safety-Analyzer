namespace MigrationSafetyAnalyzer.Core.Model;

/// <summary>Result of analyzing a project.</summary>
public sealed record AnalysisReport
{
    /// <summary>Name of the analyzed project or workspace.</summary>
    public required string ProjectName { get; init; }

    /// <summary>Resolved provider.</summary>
    public required ProviderKind Provider { get; init; }

    /// <summary>Display name of the resolved provider.</summary>
    public required string ProviderDisplayName { get; init; }

    /// <summary>Provider was detected rather than supplied.</summary>
    public bool ProviderDetected { get; init; }

    /// <summary>Number of migrations analyzed.</summary>
    public int MigrationsAnalyzed { get; init; }

    /// <summary>Number of source files scanned.</summary>
    public int FilesScanned { get; init; }

    /// <summary>All findings, sorted by severity.</summary>
    public IReadOnlyList<Finding> Findings { get; init; } = [];

    /// <summary>Number of errors.</summary>
    public int ErrorCount => Findings.Count(f => f.Severity == Severity.Error);

    /// <summary>Number of warnings.</summary>
    public int WarningCount => Findings.Count(f => f.Severity == Severity.Warning);

    /// <summary>Number of informational findings.</summary>
    public int InfoCount => Findings.Count(f => f.Severity == Severity.Info);
}
