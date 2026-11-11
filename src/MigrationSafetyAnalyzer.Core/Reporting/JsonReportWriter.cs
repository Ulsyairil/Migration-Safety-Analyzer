using System.Text.Json;
using System.Text.Json.Serialization;
using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Core.Reporting;

/// <summary>Serializes an analysis report for CI consumption.</summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Serializes a report to JSON.</summary>
    public static string Write(AnalysisReport report)
    {
        var payload = new
        {
            tool = "migration-safety-analyzer",
            version = "0.1.0",
            project = report.ProjectName,
            provider = new
            {
                name = report.ProviderDisplayName,
                detected = report.ProviderDetected,
            },
            summary = new
            {
                filesScanned = report.FilesScanned,
                migrationsAnalyzed = report.MigrationsAnalyzed,
                errors = report.ErrorCount,
                warnings = report.WarningCount,
                info = report.InfoCount,
            },
            findings = report.Findings.Select(f => new
            {
                ruleId = f.RuleId,
                title = f.Title,
                severity = f.Severity.ToString().ToLowerInvariant(),
                migration = f.MigrationId,
                file = f.FilePath,
                line = f.Line,
                lockImpact = f.LockImpact.ToString(),
                message = f.Message,
                impact = f.Impact,
                remedy = f.Remedy,
            }),
        };

        return JsonSerializer.Serialize(payload, Options);
    }
}
