using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Core.Discovery;

/// <summary>A discovered .NET project and the EF Core provider it targets.</summary>
public sealed record DiscoveredProject
{
    /// <summary>Path of the project file.</summary>
    public required string ProjectFile { get; init; }

    /// <summary>Directory containing the project file.</summary>
    public required string Directory { get; init; }

    /// <summary>Project name without extension.</summary>
    public required string Name { get; init; }

    /// <summary>Provider detected from package references, when found.</summary>
    public ProviderKind? DetectedProvider { get; init; }

    /// <summary>Package reference that identified the provider.</summary>
    public string? ProviderPackage { get; init; }
}

/// <summary>Finds .NET projects and detects their EF Core provider.</summary>
public static class ProjectDiscovery
{
    private static readonly (string Package, ProviderKind Provider)[] ProviderMarkers =
    [
        ("Npgsql.EntityFrameworkCore.PostgreSQL", ProviderKind.Postgres),
        ("Microsoft.EntityFrameworkCore.SqlServer", ProviderKind.SqlServer),
        ("Microsoft.EntityFrameworkCore.Sqlite", ProviderKind.Sqlite),
        ("Pomelo.EntityFrameworkCore.MySql", ProviderKind.MySql),
        ("MySql.EntityFrameworkCore", ProviderKind.MySql),
        ("MySqlConnector", ProviderKind.MySql),
    ];

    /// <summary>Enumerates project files under a root directory.</summary>
    public static IReadOnlyList<DiscoveredProject> Discover(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        var results = new List<DiscoveredProject>();

        foreach (var projectFile in Directory.EnumerateFiles(rootDirectory, "*.csproj", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(projectFile);
            var provider = ProviderKind.Auto;
            string? marker = null;

            foreach (var (package, kind) in ProviderMarkers)
            {
                if (text.Contains(package, StringComparison.OrdinalIgnoreCase))
                {
                    provider = kind;
                    marker = package;
                    break;
                }
            }

            var directory = Path.GetDirectoryName(projectFile) ?? rootDirectory;
            results.Add(new DiscoveredProject
            {
                ProjectFile = projectFile,
                Directory = directory,
                Name = Path.GetFileNameWithoutExtension(projectFile),
                DetectedProvider = provider == ProviderKind.Auto ? null : provider,
                ProviderPackage = marker,
            });
        }

        return results.OrderBy(p => p.ProjectFile, StringComparer.Ordinal).ToList();
    }
}
