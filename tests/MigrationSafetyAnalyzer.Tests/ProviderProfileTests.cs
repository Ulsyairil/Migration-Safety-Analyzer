using MigrationSafetyAnalyzer.Core;
using MigrationSafetyAnalyzer.Core.Model;
using Xunit;

namespace MigrationSafetyAnalyzer.Tests;

public sealed class ProviderProfileTests
{
    private const string CreateTableMigration = """
        using Microsoft.EntityFrameworkCore.Migrations;

        public partial class Initial : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "Invoices",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false),
                        Amount = table.Column<decimal>(type: "numeric", nullable: false),
                    },
                    constraints: table =>
                    {
                        table.PrimaryKey("PK_Invoices", x => x.Id);
                    });
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.DropTable(name: "Invoices");
            }
        }
        """;

    private const string AlterTypeMigration = """
        using Microsoft.EntityFrameworkCore.Migrations;

        public partial class AlterAmount : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.AlterColumn<decimal>(
                    name: "Amount",
                    table: "Invoices",
                    oldColumnType: "numeric",
                    newColumnType: "double precision",
                    oldNullable: false,
                    newNullable: false);
            }

            protected override void Down(MigrationBuilder migrationBuilder)
            {
            }
        }
        """;

    [Fact]
    public void PostgresAlterTypeIsTableRewrite()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_AlterAmount", AlterTypeMigration);

        var report = fixture.Analyze(ProviderKind.Postgres);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA005");
        Assert.Equal(LockLevel.TableRewrite, finding.LockImpact);
    }

    [Fact]
    public void SqlServerAlterTypeIsBlocking()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_AlterAmount", AlterTypeMigration);

        var report = fixture.Analyze(ProviderKind.SqlServer);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA005");
        Assert.Equal(LockLevel.Blocking, finding.LockImpact);
    }

    [Fact]
    public void SqliteAddNullableColumnIsMetadataOnly()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_AddNote", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class AddNote : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<string>(
                            name: "Note",
                            table: "Invoices",
                            type: "text",
                            nullable: true);
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropColumn(
                            name: "Note",
                            table: "Invoices");
                    }
                }
                """);

        var report = fixture.Analyze(ProviderKind.Sqlite);

        // The only finding must be the data loss on the column drop in the Down path, which is not analyzed by default.
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void SqliteForeignKeyRebuildsTable()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_AddFk", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class AddFk : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddForeignKey(
                            name: "FK_Invoices_Customers_CustomerId",
                            table: "Invoices",
                            column: "CustomerId",
                            principalTable: "Customers",
                            principalColumn: "Id");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropForeignKey(
                            name: "FK_Invoices_Customers_CustomerId",
                            table: "Invoices");
                    }
                }
                """);

        var report = fixture.Analyze(ProviderKind.Sqlite);

        // SQLite does not validate existing rows, so no finding is expected.
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSA007");
    }

    [Fact]
    public void PostgresForeignKeyValidatesExistingRows()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_AddFk", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class AddFk : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddForeignKey(
                            name: "FK_Invoices_Customers_CustomerId",
                            table: "Invoices",
                            column: "CustomerId",
                            principalTable: "Customers",
                            principalColumn: "Id");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropForeignKey(
                            name: "FK_Invoices_Customers_CustomerId",
                            table: "Invoices");
                    }
                }
                """);

        var report = fixture.Analyze(ProviderKind.Postgres);

        Assert.Contains(report.Findings, f => f.RuleId == "MSA007");
    }

    [Fact]
    public void DownDirectionAnalyzesOnlyDownPath()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_DropAmount", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class DropAmount : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropColumn(
                            name: "Amount",
                            table: "Invoices");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<decimal>(
                            name: "Amount",
                            table: "Invoices",
                            type: "numeric",
                            nullable: false,
                            defaultValue: 0m);
                    }
                }
                """);

        var analyzer = new MigrationSafetyAnalyzer.Core.MigrationSafetyAnalyzer();
        var report = analyzer.AnalyzeDirectory(
            fixture.Root,
            new AnalysisOptions { Provider = ProviderKind.Postgres, Direction = MigrationDirection.Down });

        // The Down path adds the column back with a default, which is not an error on its own.
        Assert.DoesNotContain(report.Findings, f => f.RuleId == "MSA004");
    }
}
