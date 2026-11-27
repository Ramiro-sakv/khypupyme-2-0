using Microsoft.EntityFrameworkCore.Migrations;

// Crea el esquema khypupyme, el espacio de nombres que agrupa las tablas de la aplicación dentro de PostgreSQL.
namespace KhypuPyme.Api.Persistence.Migrations;

public partial class InitialSchema : Migration
{
    // Prepara el esquema antes de que otras migraciones creen tablas dentro de él.
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("khypupyme");
    }

    // RESTRICT impide quitar el esquema si todavía contiene objetos. Por eso se debe revertir primero lo que se creó después.
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP SCHEMA khypupyme RESTRICT;");
    }
}
