using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Cli;

/// <summary>Renders an analysis report for a terminal.</summary>
public static class ConsoleReportWriter
{
    private static readonly Dictionary<Severity, string> Labels = new()
    {
        [Severity.Error] = "ERROR",
        [Severity.Warning] = "WARN ",
        [Severity.Info] = "INFO ",
    };

    /// <summary>Writes a human readable report.</summary>
    public static void Write(AnalysisReport report, TextWriter writer)
    {
        var providerNote = report.ProviderDetected ? " (detected)" : string.Empty;

        writer.WriteLine($"migration-safety — {report.ProjectName}");
        writer.WriteLine($"provider: {report.ProviderDisplayName}{providerNote}");
        writer.WriteLine($"files scanned: {report.FilesScanned}   migrations analyzed: {report.MigrationsAnalyzed}");
        writer.WriteLine();

        if (report.Findings.Count == 0)
        {
            writer.WriteLine("No safety issues found.");
            return;
        }

        string? currentMigration = null;

        foreach (var finding in report.Findings)
        {
            if (finding.MigrationId != currentMigration)
            {
                currentMigration = finding.MigrationId;
                writer.WriteLine();
                writer.WriteLine($"── {currentMigration} ──");
            }

            var location = finding.Line.HasValue ? $"line {finding.Line}" : "unknown line";
            writer.WriteLine($"  {Labels[finding.Severity]} {finding.RuleId}  {finding.Title}");
            writer.WriteLine($"         {finding.Message}");

            if (!string.IsNullOrWhiteSpace(finding.Impact))
            {
                writer.WriteLine($"         impact: {finding.Impact}");
            }

            if (!string.IsNullOrWhiteSpace(finding.Remedy))
            {
                writer.WriteLine($"         remedy: {finding.Remedy}");
            }

            writer.WriteLine($"         lock: {finding.LockImpact}   {location}");
        }

        writer.WriteLine();
        writer.WriteLine($"errors: {report.ErrorCount}   warnings: {report.WarningCount}   info: {report.InfoCount}");
    }
}
