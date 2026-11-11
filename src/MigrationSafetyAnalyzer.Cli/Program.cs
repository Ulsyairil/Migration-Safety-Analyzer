using System.CommandLine;
using MigrationSafetyAnalyzer.Core;
using MigrationSafetyAnalyzer.Core.Model;
using MigrationSafetyAnalyzer.Core.Reporting;

namespace MigrationSafetyAnalyzer.Cli;

/// <summary>Entry point of the migration-safety command line tool.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var pathOption = new Option<string>("--path", "-p")
        {
            Description = "Path to the project or directory that contains EF Core migrations.",
            DefaultValueFactory = _ => Directory.GetCurrentDirectory(),
        };

        var providerOption = new Option<string>("--provider")
        {
            Description = "Database provider: auto, postgres, sqlserver, sqlite, mysql.",
            DefaultValueFactory = _ => "auto",
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "Emit the report as JSON for CI consumption.",
        };

        var failOnOption = new Option<string>("--fail-on")
        {
            Description = "Exit with code 1 when a finding of this severity or higher exists: error, warning, info, none.",
            DefaultValueFactory = _ => "error",
        };

        var minSeverityOption = new Option<string>("--min-severity")
        {
            Description = "Hide findings below this severity: error, warning, info.",
            DefaultValueFactory = _ => "info",
        };

        var upOnlyOption = new Option<bool>("--up")
        {
            Description = "Analyze only the Up path of each migration. This is the default.",
        };

        var downOnlyOption = new Option<bool>("--down")
        {
            Description = "Analyze only the Down path of each migration.",
        };

        var bothOption = new Option<bool>("--both")
        {
            Description = "Analyze both the Up and Down path of each migration.",
        };

        var rootCommand = new RootCommand(
            "Static safety analyzer for Entity Framework Core migrations. Grades lock impact, table rewrites and data loss per database provider.")
        {
            pathOption,
            providerOption,
            jsonOption,
            failOnOption,
            minSeverityOption,
            upOnlyOption,
            downOnlyOption,
            bothOption,
        };

        rootCommand.SetAction(parseResult =>
        {
            var path = parseResult.GetValue(pathOption) ?? Directory.GetCurrentDirectory();
            var providerText = parseResult.GetValue(providerOption) ?? "auto";
            var json = parseResult.GetValue(jsonOption);
            var failOnText = parseResult.GetValue(failOnOption) ?? "error";
            var minSeverityText = parseResult.GetValue(minSeverityOption) ?? "info";
            var upOnly = parseResult.GetValue(upOnlyOption);
            var downOnly = parseResult.GetValue(downOnlyOption);
            var both = parseResult.GetValue(bothOption);

            if (!Directory.Exists(path))
            {
                Console.Error.WriteLine($"error: path not found: {path}");
                return 2;
            }

            if (!TryParseProvider(providerText, out var provider))
            {
                Console.Error.WriteLine($"error: unknown provider '{providerText}'. Use auto, postgres, sqlserver, sqlite or mysql.");
                return 2;
            }

            if (!TryParseSeverity(minSeverityText, out var minSeverity))
            {
                Console.Error.WriteLine($"error: unknown severity '{minSeverityText}'. Use error, warning or info.");
                return 2;
            }

            if (!TryParseSeverity(failOnText, out var failOn) && failOnText != "none")
            {
                Console.Error.WriteLine($"error: unknown severity '{failOnText}'. Use error, warning, info or none.");
                return 2;
            }

            if (upOnly && downOnly || (upOnly && both) || (downOnly && both))
            {
                Console.Error.WriteLine("error: --up, --down and --both are mutually exclusive.");
                return 2;
            }

            var direction = downOnly
                ? MigrationDirection.Down
                : both
                    ? MigrationDirection.Both
                    : MigrationDirection.Up;

            var options = new AnalysisOptions
            {
                Provider = provider,
                MinimumSeverity = minSeverity,
                Direction = direction,
            };

            var analyzer = new Core.MigrationSafetyAnalyzer();
            var report = analyzer.AnalyzeDirectory(path, options);

            if (json)
            {
                Console.WriteLine(JsonReportWriter.Write(report));
            }
            else
            {
                ConsoleReportWriter.Write(report, Console.Out);
            }

            if (failOnText == "none")
            {
                return 0;
            }

            return report.Findings.Any(f => f.Severity >= failOn) ? 1 : 0;
        });

        return await rootCommand.Parse(args).InvokeAsync();
    }

    private static bool TryParseProvider(string text, out ProviderKind provider)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "auto":
                provider = ProviderKind.Auto;
                return true;
            case "postgres":
            case "postgresql":
            case "npgsql":
                provider = ProviderKind.Postgres;
                return true;
            case "sqlserver":
            case "mssql":
                provider = ProviderKind.SqlServer;
                return true;
            case "sqlite":
                provider = ProviderKind.Sqlite;
                return true;
            case "mysql":
            case "mariadb":
                provider = ProviderKind.MySql;
                return true;
            default:
                provider = ProviderKind.Auto;
                return false;
        }
    }

    private static bool TryParseSeverity(string text, out Severity severity)
    {
        switch (text.Trim().ToLowerInvariant())
        {
            case "error":
                severity = Severity.Error;
                return true;
            case "warning":
            case "warn":
                severity = Severity.Warning;
                return true;
            case "info":
                severity = Severity.Info;
                return true;
            default:
                severity = Severity.Info;
                return false;
        }
    }
}
