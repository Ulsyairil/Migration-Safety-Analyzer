using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

public partial class DropInvoiceNote : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Drop the column, then add it back: every stored value is lost without an error.
        migrationBuilder.DropColumn(
            name: "Note",
            table: "Invoices");

        migrationBuilder.AddColumn<string>(
            name: "Note",
            table: "Invoices",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        // Unbounded delete.
        migrationBuilder.Sql("DELETE FROM \"Customers\";");

        // No Down override: the schema cannot be rolled back.
    }
}
