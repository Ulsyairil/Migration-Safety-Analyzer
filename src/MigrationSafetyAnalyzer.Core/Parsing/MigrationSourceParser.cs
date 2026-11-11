using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MigrationSafetyAnalyzer.Core.Model;
using OperationKind = MigrationSafetyAnalyzer.Core.Model.OperationKind;

namespace MigrationSafetyAnalyzer.Core.Parsing;

/// <summary>Parses EF Core migration source files into an operation model.</summary>
public sealed class MigrationSourceParser
{
    private static readonly HashSet<string> KnownOperations = new(StringComparer.Ordinal)
    {
        "AddColumn", "AlterColumn", "DropColumn", "RenameColumn", "RenameTable",
        "AddForeignKey", "DropForeignKey", "AddPrimaryKey", "DropPrimaryKey",
        "AddIndex", "DropIndex", "AddCheckConstraint", "DropCheckConstraint",
        "CreateTable", "DropTable", "AlterDatabase", "Sql",
        "InsertData", "UpdateData", "DeleteData",
    };

    /// <summary>Parses one migration file. Returns an empty list when the file has no migration class.</summary>
    public IReadOnlyList<MigrationModel> ParseFile(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Latest));
        var root = tree.GetRoot();

        var models = new List<MigrationModel>();

        foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (!IsMigrationClass(cls))
            {
                continue;
            }

            var className = cls.Identifier.ValueText;
            var up = ExtractOperations(cls, "Up");
            var down = ExtractOperations(cls, "Down");

            if (up.Count == 0 && down.Count == 0)
            {
                continue;
            }

            models.Add(new MigrationModel
            {
                Id = ExtractMigrationId(cls, className, filePath),
                ClassName = className,
                FilePath = filePath,
                Up = up,
                Down = down,
                HasDown = down.Count > 0,
            });
        }

        return models;
    }

    private static bool IsMigrationClass(ClassDeclarationSyntax cls)
    {
        foreach (var attr in cls.AttributeLists.SelectMany(a => a.Attributes))
        {
            var name = attr.Name.ToString();
            if (name.EndsWith("MigrationAttribute", StringComparison.Ordinal)
                || name.EndsWith("DbMigrationAttribute", StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var baseType in cls.BaseList?.Types ?? default)
        {
            if (baseType.Type.ToString().EndsWith("Migration", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Derives the migration identifier. EF Core writes the timestamp into the file name and, in
    /// hand written migrations, into the class name as well. The file name wins because the class
    /// name does not always carry a timestamp.
    /// </summary>
    private static string ExtractMigrationId(ClassDeclarationSyntax cls, string className, string filePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        var underscore = fileName.IndexOf('_');
        if (underscore > 0 && fileName[..underscore].All(char.IsDigit))
        {
            return fileName[..underscore];
        }

        underscore = className.IndexOf('_');
        if (underscore > 0 && className[..underscore].All(char.IsDigit))
        {
            return className[..underscore];
        }

        var attribute = cls.AttributeLists
            .SelectMany(a => a.Attributes)
            .FirstOrDefault(a => a.Name.ToString().EndsWith("MigrationAttribute", StringComparison.Ordinal));

        if (attribute?.ArgumentList?.Arguments.Count > 0
            && attribute.ArgumentList.Arguments[0].Expression is LiteralExpressionSyntax literal
            && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            return literal.Token.ValueText;
        }

        return className;
    }

    private static List<MigrationOperation> ExtractOperations(ClassDeclarationSyntax cls, string methodName)
    {
        var method = cls.Members
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(m => m.Identifier.ValueText == methodName
                                 && m.ParameterList.Parameters.Count <= 1);

        if (method is null)
        {
            return [];
        }

        var results = new List<MigrationOperation>();

        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            var op = ToOperation(invocation, methodName);
            if (op is not null)
            {
                results.Add(op);
            }
        }

        return results;
    }

    private static MigrationOperation? ToOperation(InvocationExpressionSyntax invocation, string direction)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax member)
        {
            return null;
        }

        var methodName = member.Name.Identifier.ValueText;
        if (!KnownOperations.Contains(methodName))
        {
            return null;
        }

        // EF Core convention: the builder instance is named migrationBuilder.
        if (member.Expression is not IdentifierNameSyntax identifier
            || !identifier.Identifier.ValueText.Equals("migrationBuilder", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var named = BuildNamedArguments(invocation.ArgumentList.Arguments);
        var kind = MapKind(methodName);

        // Raw SQL is the first positional argument of MigrationBuilder.Sql.
        if (kind == OperationKind.Sql && !named.ContainsKey("sql") && invocation.ArgumentList.Arguments.Count > 0)
        {
            named["sql"] = invocation.ArgumentList.Arguments[0];
        }

        // CreateTable and DropTable name the table with "name", every other op uses "table".
        var table = kind is OperationKind.CreateTable or OperationKind.DropTable
            ? StringArg(named, "name") ?? StringArg(named, "table")
            : StringArg(named, "table");

        string? column;
        switch (kind)
        {
            case OperationKind.AddColumn:
            case OperationKind.AlterColumn:
            case OperationKind.DropColumn:
            case OperationKind.RenameColumn:
                column = StringArg(named, "name");
                break;

            case OperationKind.AddForeignKey:
            case OperationKind.DropForeignKey:
                column = StringArg(named, "column");
                break;

            default:
                column = null;
                break;
        }

        // AlterColumn exposes oldColumnType/newColumnType, plus oldType/newType store types.
        var oldColumnType = kind == OperationKind.AlterColumn
            ? StringArg(named, "oldColumnType") ?? StringArg(named, "oldType")
            : null;

        var newColumnType = kind == OperationKind.AlterColumn
            ? StringArg(named, "newColumnType") ?? StringArg(named, "newType")
            : null;

        // EF Core 9+ uses the "type" argument for both AddColumn and AlterColumn.
        var storeType = StringArg(named, "type") ?? StringArg(named, "columnType");

        var typeChanged = false;
        var typeVerified = false;

        if (kind == OperationKind.AlterColumn)
        {
            typeChanged = !string.Equals(oldColumnType, newColumnType, StringComparison.OrdinalIgnoreCase);
            typeVerified = oldColumnType is not null && newColumnType is not null;
        }

        var hasDefault = named.ContainsKey("defaultValue")
                        || named.ContainsKey("defaultValueSql")
                        || named.ContainsKey("computedColumnSql");

        var defaultValue = StringArg(named, "defaultValue") ?? StringArg(named, "defaultValueSql")
                           ?? StringArg(named, "computedColumnSql");

        var defaultIsVolatile = false;
        if (named.TryGetValue("defaultValueSql", out var sqlArg) && sqlArg.Expression is not null)
        {
            defaultIsVolatile = IsVolatileExpression(sqlArg.Expression);
        }
        else if (named.TryGetValue("defaultValue", out var valueArg) && valueArg.Expression is not null)
        {
            defaultIsVolatile = IsVolatileExpression(valueArg.Expression);
        }

        var createdColumns = kind == OperationKind.CreateTable
            ? ParseCreateTableColumns(invocation)
            : [];

        return new MigrationOperation
        {
            Kind = kind,
            MethodName = methodName,
            Direction = direction,
            Table = table,
            Column = column,
            NewName = StringArg(named, "newName"),
            Nullable = BoolArg(named, "nullable"),
            OldNullable = BoolArg(named, "oldNullable"),
            Unique = BoolArg(named, "unique"),
            HasDefault = hasDefault,
            DefaultValue = defaultValue,
            DefaultIsVolatile = defaultIsVolatile,
            ColumnType = kind == OperationKind.AlterColumn ? newColumnType : storeType,
            OldColumnType = oldColumnType,
            TypeChanged = typeChanged,
            TypeChangeVerified = typeVerified,
            PrincipalTable = StringArg(named, "principalTable"),
            PrincipalColumn = StringArg(named, "principalColumn"),
            RawSql = StringArg(named, "sql"),
            CreatedColumns = createdColumns,
            Line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
        };
    }

    /// <summary>
    /// Reads the column names declared inside a CreateTable lambda:
    /// <c>columns: table =&gt; new { id = table.Column&lt;int&gt;(nullable: false) }</c>.
    /// The body is an anonymous object, so members are anonymous object member declarations.
    /// </summary>
    private static IReadOnlyList<string> ParseCreateTableColumns(InvocationExpressionSyntax invocation)
    {
        var columnsArgument = invocation.ArgumentList.Arguments
            .FirstOrDefault(a => a.NameColon?.Name.Identifier.ValueText == "columns");

        if (columnsArgument?.Expression is not LambdaExpressionSyntax lambda)
        {
            return [];
        }

        var names = new List<string>();

        foreach (var member in lambda.Body.DescendantNodes().OfType<AnonymousObjectMemberDeclaratorSyntax>())
        {
            names.Add(member.NameEquals?.Name.Identifier.ValueText
                      ?? member.Expression.DescendantNodes().OfType<IdentifierNameSyntax>()
                          .FirstOrDefault()?.Identifier.ValueText
                      ?? string.Empty);
        }

        return [.. names.Where(n => n.Length > 0)];
    }

    private static Dictionary<string, ArgumentSyntax> BuildNamedArguments(SeparatedSyntaxList<ArgumentSyntax> args)
    {
        var named = new Dictionary<string, ArgumentSyntax>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (arg.NameColon?.Name.Identifier.ValueText is { } name)
            {
                named[name] = arg;
            }
        }

        return named;
    }

    private static OperationKind MapKind(string methodName) => methodName switch
    {
        "AddColumn" => OperationKind.AddColumn,
        "AlterColumn" => OperationKind.AlterColumn,
        "DropColumn" => OperationKind.DropColumn,
        "RenameColumn" => OperationKind.RenameColumn,
        "RenameTable" => OperationKind.RenameTable,
        "AddForeignKey" => OperationKind.AddForeignKey,
        "DropForeignKey" => OperationKind.DropForeignKey,
        "AddPrimaryKey" => OperationKind.AddPrimaryKey,
        "DropPrimaryKey" => OperationKind.DropPrimaryKey,
        "AddIndex" => OperationKind.AddIndex,
        "DropIndex" => OperationKind.DropIndex,
        "AddCheckConstraint" => OperationKind.AddCheckConstraint,
        "DropCheckConstraint" => OperationKind.DropCheckConstraint,
        "CreateTable" => OperationKind.CreateTable,
        "DropTable" => OperationKind.DropTable,
        "AlterDatabase" => OperationKind.AlterDatabase,
        "Sql" => OperationKind.Sql,
        "InsertData" => OperationKind.InsertData,
        "UpdateData" => OperationKind.UpdateData,
        "DeleteData" => OperationKind.DeleteData,
        _ => OperationKind.Unknown,
    };

    private static string? StringArg(Dictionary<string, ArgumentSyntax> named, string name)
    {
        if (!named.TryGetValue(name, out var arg))
        {
            return null;
        }

        return arg.Expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
                => literal.Token.ValueText,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression)
                => null,
            _ => null,
        };
    }

    private static bool? BoolArg(Dictionary<string, ArgumentSyntax> named, string name)
    {
        if (!named.TryGetValue(name, out var arg))
        {
            return null;
        }

        return arg.Expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.TrueLiteralExpression) => true,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.FalseLiteralExpression) => false,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression) => null,
            _ => null,
        };
    }

    private static bool IsVolatileExpression(ExpressionSyntax expression)
    {
        var text = expression.ToString();
        return text.Contains("now()", StringComparison.OrdinalIgnoreCase)
               || text.Contains("newid", StringComparison.OrdinalIgnoreCase)
               || text.Contains("getdate", StringComparison.OrdinalIgnoreCase)
               || text.Contains("current_timestamp", StringComparison.OrdinalIgnoreCase)
               || text.Contains("uuid_generate", StringComparison.OrdinalIgnoreCase)
               || text.Contains("DateTime.Now", StringComparison.Ordinal)
               || text.Contains("DateTime.UtcNow", StringComparison.Ordinal)
               || text.Contains("random", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Discovers candidate migration source files under a directory.</summary>
    public static IReadOnlyList<string> DiscoverMigrationFiles(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(rootDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(IsLikelyMigrationFile)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsLikelyMigrationFile(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var directoryName = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty;

        if (fileName.Contains("ModelSnapshot", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (directoryName.Contains("Migrations", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Timestamp-prefixed names are the EF Core default even outside a Migrations folder.
        var underscore = fileName.IndexOf('_');
        if (underscore <= 0)
        {
            return false;
        }

        var prefix = fileName[..underscore];
        return prefix.Length >= 14 && prefix.All(char.IsDigit);
    }
}
