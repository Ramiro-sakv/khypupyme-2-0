using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

// Crea el almacén de claves de protección de datos que usa el perfil de piloto cuando está configurado.
namespace KhypuPyme.Api.Persistence.Migrations;

[DbContext(typeof(KhypuPymeDbContext))]
[Migration("20261119050000_PilotKeyRing")]
public sealed class PilotKeyRing : Migration
{
    // Las claves persistentes permiten que los datos protegidos sigan siendo legibles por la aplicación después de reiniciar.
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("CREATE TABLE khypupyme.pilot_key_ring (name text PRIMARY KEY, payload text NOT NULL);");

    // Quitar la tabla también elimina las claves almacenadas en ella.
    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE khypupyme.pilot_key_ring;");
}
