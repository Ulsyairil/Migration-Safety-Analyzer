using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

public partial class LinkInvoiceCustomer : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Foreign key validation scans both tables while holding a lock.
        migrationBuilder.AddForeignKey(
            name: "FK_Invoices_Customers_CustomerId",
            table: "Invoices",
            column: "CustomerId",
            principalTable: "Customers",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);

        // Plain CREATE INDEX blocks writes for the duration of the build.
        migrationBuilder.Sql("CREATE INDEX IX_Invoices_CustomerId ON \"Invoices\" (\"CustomerId\");");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_Invoices_Customers_CustomerId",
            table: "Invoices");

        migrationBuilder.Sql("DROP INDEX IX_Invoices_CustomerId;");
    }
}
