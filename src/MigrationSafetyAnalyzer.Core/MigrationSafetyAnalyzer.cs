using MigrationSafetyAnalyzer.Core.Analysis;
using MigrationSafetyAnalyzer.Core.Discovery;
using MigrationSafetyAnalyzer.Core.Model;
using MigrationSafetyAnalyzer.Core.Parsing;

namespace MigrationSafetyAnalyzer.Core;

/// <summary>Which side of a migration to analyze.</summary>
public enum MigrationDirection
{
    /// <summary>Analyze the Up path only. This is the default.</summary>
    Up = 0,

    /// <summary>Analyze the Down path only.</summary>
    Down = 1,

    /// <summary>Analyze both paths.</summary>
    Both = 2,
}

/// <summary>Options controlling an analysis run.</summary>
public sealed record AnalysisOptions
{
    /// <summary>Provider to grade against. Auto detects it from package references.</summary>
    public ProviderKind Provider { get; init; } = ProviderKind.Auto;

    /// <summary>Which side of each migration to analyze.</summary>
    public MigrationDirection Direction { get; init; } = MigrationDirection.Up;

    /// <summary>Minimum severity to report.</summary>
    public Severity MinimumSeverity { get; init; } = Severity.Info;
}

/// <summary>Runs the migration safety analysis over a project or directory.</summary>
public sealed class MigrationSafetyAnalyzer
{
    private static readonly IMigrationRule[] Rules =
    [
        new DropAndReAddRule(),
        new NonNullableAddColumnRule(),
        new VolatileDefaultRule(),
        new DataLossRule(),
        new TypeChangeRule(),
        new TightenNullabilityRule(),
        new ForeignKeyValidationRule(),
        new PrimaryKeyRule(),
        new DownMigrationRule(),
        new UniqueConstraintRule(),
        new IndexBuildRule(),
        new RawSqlRule(),
    ];

    private readonly MigrationSourceParser _parser = new();

    /// <summary>Analyzes a directory that contains one or more .NET projects.</summary>
    public AnalysisReport AnalyzeDirectory(string rootDirectory, AnalysisOptions options)
    {
        var projects = ProjectDiscovery.Discover(rootDirectory);
        if (projects.Count == 0)
        {
            return new AnalysisReport
            {
                ProjectName = Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar)),
                Provider = ResolveProvider(options, null),
                ProviderDisplayName = ProviderProfiles.For(ResolveProvider(options, null)).DisplayName,
                ProviderDetected = options.Provider == ProviderKind.Auto,
            };
        }

        var provider = ResolveProvider(options, projects);

        var allFindings = new List<Finding>();
        var migrationsAnalyzed = 0;
        var filesScanned = 0;

        foreach (var project in projects)
        {
            var migrationFiles = MigrationSourceParser.DiscoverMigrationFiles(project.Directory);
            filesScanned += migrationFiles.Count;

            var models = new List<MigrationModel>();
            foreach (var file in migrationFiles)
            {
                models.AddRange(_parser.ParseFile(file));
            }

            models = models
                .OrderBy(m => m.Id, StringComparer.Ordinal)
                .ThenBy(m => m.FilePath, StringComparer.Ordinal)
                .ToList();

            var schema = new SchemaTracker();

            foreach (var migration in models)
            {
                var operations = SelectOperations(migration, options);
                if (operations.Count == 0)
                {
                    continue;
                }

                migrationsAnalyzed++;

                // Tables created inside this migration are empty, so NOT NULL rules must not fire on them.
                var tablesCreatedHere = operations
                    .Where(o => o.Kind == OperationKind.CreateTable && o.Table is not null)
                    .Select(o => o.Table!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var op in operations)
                {
                    var isNewTableColumn = op.Table is not null && tablesCreatedHere.Contains(op.Table);

                    var context = new RuleContext
                    {
                        Migration = migration,
                        Operation = op with { IsNewTableColumn = isNewTableColumn },
                        Profile = ProviderProfiles.For(provider),
                        Schema = schema,
                        Siblings = operations,
                        IsFirstOperation = ReferenceEquals(op, operations[0]),
                    };

                    foreach (var rule in Rules)
                    {
                        foreach (var finding in rule.Evaluate(context))
                        {
                            allFindings.Add(finding);
                        }
                    }

                    ApplyToSchema(schema, op);
                }
            }
        }

        var filtered = allFindings
            .Where(f => f.Severity >= options.MinimumSeverity)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.MigrationId, StringComparer.Ordinal)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.Line ?? 0)
            .ToList();

        return new AnalysisReport
        {
            ProjectName = projects.Count == 1 ? projects[0].Name : Path.GetFileName(rootDirectory.TrimEnd(Path.DirectorySeparatorChar)),
            Provider = provider,
            ProviderDisplayName = ProviderProfiles.For(provider).DisplayName,
            ProviderDetected = options.Provider == ProviderKind.Auto,
            MigrationsAnalyzed = migrationsAnalyzed,
            FilesScanned = filesScanned,
            Findings = filtered,
        };
    }

    private static List<MigrationOperation> SelectOperations(MigrationModel migration, AnalysisOptions options)
        => options.Direction switch
        {
            MigrationDirection.Down => [.. migration.Down],
            MigrationDirection.Both => [.. migration.Up, .. migration.Down],
            _ => [.. migration.Up],
        };

    private static void ApplyToSchema(SchemaTracker schema, MigrationOperation op)
    {
        if (op.Table is null)
        {
            return;
        }

        switch (op.Kind)
        {
            case OperationKind.CreateTable:
                schema.CreateTable(op.Table, op.CreatedColumns);
                break;

            case OperationKind.DropTable:
                schema.DropTable(op.Table);
                break;

            case OperationKind.AddColumn:
                schema.AddColumn(op.Table, op.Column ?? string.Empty);
                break;

            case OperationKind.DropColumn:
                if (op.Column is not null)
                {
                    schema.DropColumn(op.Table, op.Column);
                }

                break;

            case OperationKind.RenameColumn:
                if (op.Column is not null && op.NewName is not null)
                {
                    schema.RenameColumn(op.Table, op.Column, op.NewName);
                }

                break;

            case OperationKind.RenameTable:
                if (op.NewName is not null)
                {
                    schema.DropTable(op.Table);
                    schema.CreateTable(op.NewName, []);
                }

                break;
        }
    }

    private static ProviderKind ResolveProvider(AnalysisOptions options, IReadOnlyList<DiscoveredProject>? projects)
    {
        if (options.Provider != ProviderKind.Auto)
        {
            return options.Provider;
        }

        if (projects is null)
        {
            return ProviderKind.Postgres;
        }

        foreach (var project in projects)
        {
            if (project.DetectedProvider is { } detected)
            {
                return detected;
            }
        }

        return ProviderKind.Postgres;
    }

    private sealed class SchemaKeyComparer : IEqualityComparer<(string Table, string Column)>
    {
        public static SchemaKeyComparer Instance { get; } = new();

        public bool Equals((string Table, string Column) x, (string Table, string Column) y)
            => string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Column, y.Column, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Table, string Column) obj)
            => HashCode.Combine(
                obj.Table?.ToLowerInvariant() ?? string.Empty,
                obj.Column?.ToLowerInvariant() ?? string.Empty);
    }
}
