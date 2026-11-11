using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

public partial class AddInvoiceStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Not nullable and no default: fails or rewrites the whole table.
        migrationBuilder.AddColumn<int>(
            name: "Status",
            table: "Invoices",
            type: "integer",
            nullable: false);

        // Volatile default: PostgreSQL rewrites every existing row.
        migrationBuilder.AddColumn<DateTime>(
            name: "SettledAt",
            table: "Invoices",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "now()");

        // Nullable with a constant default: safe on PostgreSQL 11+.
        migrationBuilder.AddColumn<string>(
            name: "Note",
            table: "Invoices",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Status", table: "Invoices");
        migrationBuilder.DropColumn(name: "SettledAt", table: "Invoices");
        migrationBuilder.DropColumn(name: "Note", table: "Invoices");
    }
}
