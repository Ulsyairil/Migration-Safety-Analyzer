using MigrationSafetyAnalyzer.Core;
using MigrationSafetyAnalyzer.Core.Model;

namespace MigrationSafetyAnalyzer.Tests;

/// <summary>Writes migration source files into a temporary directory and analyzes them.</summary>
public sealed class MigrationFixture : IDisposable
{
    private readonly string _root;

    public MigrationFixture()
    {
        _root = Path.Combine(Path.GetTempPath(), "msa-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "App", "Migrations"));
        File.WriteAllText(
            Path.Combine(_root, "App", "App.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.0.0" />
              </ItemGroup>
            </Project>
            """);
    }

    public string Root => _root;

    public string MigrationPath => Path.Combine(_root, "App", "Migrations");

    public MigrationFixture WithMigration(string className, string body)
    {
        var path = Path.Combine(MigrationPath, $"{className}.cs");
        File.WriteAllText(path, body);
        return this;
    }

    public AnalysisReport Analyze(ProviderKind provider = ProviderKind.Auto)
    {
        var analyzer = new Core.MigrationSafetyAnalyzer();
        return analyzer.AnalyzeDirectory(_root, new AnalysisOptions { Provider = provider });
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
