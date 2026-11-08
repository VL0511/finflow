using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FinFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionAccountIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Transactions_DestinationAccount_CreatedAt",
                table: "Transactions",
                columns: new[] { "DestinationAccount", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_SourceAccount_CreatedAt",
                table: "Transactions",
                columns: new[] { "SourceAccount", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_DestinationAccount_CreatedAt",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_SourceAccount_CreatedAt",
                table: "Transactions");
        }
    }
}
