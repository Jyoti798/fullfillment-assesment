using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fulfillment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Products_LowStock",
                table: "Products",
                column: "StockQuantity",
                filter: "\"IsActive\" AND \"StockQuantity\" <= \"ReorderThreshold\"");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Alerts_ProductId_CreatedAt",
                table: "Alerts",
                columns: new[] { "ProductId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_LowStock",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Alerts_ProductId_CreatedAt",
                table: "Alerts");
        }
    }
}
