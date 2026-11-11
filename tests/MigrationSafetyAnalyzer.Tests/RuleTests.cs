using MigrationSafetyAnalyzer.Core.Model;
using Xunit;

namespace MigrationSafetyAnalyzer.Tests;

public sealed class RuleTests
{
    private const string CreateTableMigration = """
        using Microsoft.EntityFrameworkCore.Migrations;
        using Microsoft.EntityFrameworkCore.Infrastructure;

        public partial class Initial : Migration
        {
            protected override void Up(MigrationBuilder migrationBuilder)
            {
                migrationBuilder.CreateTable(
                    name: "Invoices",
                    columns: table => new
                    {
                        Id = table.Column<int>(type: "integer", nullable: false)
                            .Annotation("Npgsql:ValueGenerationStrategy", "IdentityByDefaultColumn"),
                        CustomerId = table.Column<int>(type: "integer", nullable: false),
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

    [Fact]
    public void CreatesTable_ProducesNoFindings()
    {
        using var fixture = new MigrationFixture().WithMigration("20240101000000_Initial", CreateTableMigration);

        var report = fixture.Analyze();

        Assert.Empty(report.Findings);
    }

    [Fact]
    public void AddsNotNullColumnWithoutDefault_IsError()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_AddStatus", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class AddStatus : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<int>(
                            name: "Status",
                            table: "Invoices",
                            type: "integer",
                            nullable: false);
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropColumn(
                            name: "Status",
                            table: "Invoices");
                    }
                }
                """);

        var report = fixture.Analyze();

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA002");
        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("Status", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DropsAndReAddsColumn_IsError()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_DropAndReAdd", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class DropAndReAdd : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropColumn(
                            name: "Amount",
                            table: "Invoices");

                        migrationBuilder.AddColumn<decimal>(
                            name: "Amount",
                            table: "Invoices",
                            type: "numeric",
                            nullable: false,
                            defaultValue: 0m);
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """);

        var report = fixture.Analyze();

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA001");
        Assert.Equal(Severity.Error, finding.Severity);
    }

    [Fact]
    public void DropsColumn_IsError()
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

        var report = fixture.Analyze();

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA004");
        Assert.Equal(LockLevel.DataLoss, finding.LockImpact);
    }

    [Fact]
    public void VolatileDefault_IsWarning()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_AddCreatedAt", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class AddCreatedAt : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<DateTime>(
                            name: "CreatedAt",
                            table: "Invoices",
                            type: "timestamp with time zone",
                            nullable: false,
                            defaultValueSql: "now()");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropColumn(
                            name: "CreatedAt",
                            table: "Invoices");
                    }
                }
                """);

        var report = fixture.Analyze();

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA003");
        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void MissingDown_IsWarning()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_NoDown", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class NoDown : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.AddColumn<int>(
                            name: "Status",
                            table: "Invoices",
                            type: "integer",
                            nullable: true);
                    }
                }
                """);

        var report = fixture.Analyze();

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA009");
        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void RawSqlDeleteWithoutWhere_IsError()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_Purge", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class Purge : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.Sql("DELETE FROM \"Invoices\";");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                    }
                }
                """);

        var report = fixture.Analyze();

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA022");
        Assert.Equal(Severity.Error, finding.Severity);
    }

    [Fact]
    public void RawSqlCreateIndexWithoutConcurrently_IsWarningOnPostgres()
    {
        using var fixture = new MigrationFixture()
            .WithMigration("20240101000000_Initial", CreateTableMigration)
            .WithMigration("20240202000000_Index", """
                using Microsoft.EntityFrameworkCore.Migrations;

                public partial class AddIndex : Migration
                {
                    protected override void Up(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.Sql("CREATE INDEX IX_Invoices_CustomerId ON \"Invoices\" (\"CustomerId\");");
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.Sql("DROP INDEX IX_Invoices_CustomerId;");
                    }
                }
                """);

        var report = fixture.Analyze(ProviderKind.Postgres);

        Assert.Contains(report.Findings, f => f.RuleId == "MSA024" && f.Severity == Severity.Warning);
    }

    [Fact]
    public void AddsForeignKeyToPopulatedTable_IsWarning()
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
                            principalColumn: "Id",
                            onDelete: ReferentialAction.Cascade);
                    }

                    protected override void Down(MigrationBuilder migrationBuilder)
                    {
                        migrationBuilder.DropForeignKey(
                            name: "FK_Invoices_Customers_CustomerId",
                            table: "Invoices");
                    }
                }
                """);

        var report = fixture.Analyze();

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA007");
        Assert.Equal(Severity.Warning, finding.Severity);
    }

    [Fact]
    public void DetectsProviderFromPackageReference()
    {
        using var fixture = new MigrationFixture().WithMigration("20240101000000_Initial", CreateTableMigration);

        var report = fixture.Analyze();

        Assert.Equal(ProviderKind.Postgres, report.Provider);
        Assert.True(report.ProviderDetected);
    }

    [Fact]
    public void ExplicitProviderOverridesDetection()
    {
        using var fixture = new MigrationFixture().WithMigration("20240101000000_Initial", CreateTableMigration);

        var report = fixture.Analyze(ProviderKind.Sqlite);

        Assert.Equal(ProviderKind.Sqlite, report.Provider);
        Assert.False(report.ProviderDetected);
    }

    [Fact]
    public void SqliteProfilesDropColumnAsTableRewrite()
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
                    }
                }
                """);

        var report = fixture.Analyze(ProviderKind.Sqlite);

        var finding = Assert.Single(report.Findings, f => f.RuleId == "MSA004");
        Assert.Equal(LockLevel.DataLoss, finding.LockImpact);
    }
}
