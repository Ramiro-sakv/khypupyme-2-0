using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// Añade el número de meses de uso tras abrir el producto, un dato distinto de la fecha de vencimiento del lote.
namespace KhypuPyme.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryProfileDetails : Migration
    {
        /// <inheritdoc />
        // La restricción limita el rango de meses admitidos para que PostgreSQL también valide ese dato.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AfterOpeningMonths",
                schema: "khypupyme",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "products_after_opening_months",
                schema: "khypupyme",
                table: "products",
                sql: " \"AfterOpeningMonths\" IS NULL OR (\"Brand\" IS NOT NULL AND \"AfterOpeningMonths\" BETWEEN 1 AND 120) ");
        }

        /// <inheritdoc />
        // Retira la restricción y el campo de duración tras apertura.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "products_after_opening_months",
                schema: "khypupyme",
                table: "products");

            migrationBuilder.DropColumn(
                name: "AfterOpeningMonths",
                schema: "khypupyme",
                table: "products");
        }
    }
}
