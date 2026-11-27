# Arquitectura y mantenimiento de KhypuPyme 2.0

KhypuPyme usa una interfaz React y TypeScript, una API ASP.NET Core en C# y PostgreSQL. El servidor es un **monolito modular**. Se ejecuta como una sola API y comparte la transacción de base de datos cuando una venta cambia existencias, Caja y comprobantes.

## Comparación con proyectos de referencia

| Referencia | Cómo se organiza | Qué hacemos en KhypuPyme |
| --- | --- | --- |
| InvenTree | El sistema de inventario separa el backend, la API REST y la interfaz. Mantiene documentación de instalación y operación. | `server` y `web` tienen entradas y dependencias propias. React consume la API. Conservamos controles especializados de medidas, lotes, variantes y series dentro de los módulos de rubro. |
| eShopOnWeb, ejemplo de Microsoft | Despliega ASP.NET como una aplicación y divide presentación, núcleo de negocio, infraestructura y pruebas en proyectos. | Separamos rutas, operaciones, consultas y persistencia por responsabilidad dentro del proyecto de la API. Algunas operaciones siguen usando EF Core directamente. Separar cálculos puros en bibliotecas tiene sentido cuando necesitan reutilizarse o probarse de forma independiente. |
| Documentación de GitHub Actions y NuGet | Define trabajos automáticos para compilar, verificar dependencias y proteger la cadena de suministro. | El workflow de revisión usa permisos de solo lectura. Verifica las dependencias fijadas, OpenAPI, compilaciones, lint, tipos y secretos en Git. Las pruebas comerciales de PostgreSQL siguen un recorrido aislado en Windows. |

Fuentes [Arquitectura de InvenTree](https://docs.inventree.org/en/latest/develop/architecture/), [arquitectura de eShopOnWeb](https://github.com/dotnet-architecture/eShopOnWeb/wiki/Architecture), [uso seguro de GitHub Actions](https://docs.github.com/en/actions/reference/security/secure-use) y [auditoría de NuGet](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages).

## Complementos y operación

- **Inventario y rubros.** InvenTree ofrece un sistema de plugins y trabajadores en segundo plano. En KhypuPyme, los rubros son módulos del mismo servidor. Una venta conserva una sola transacción con stock, Caja y comprobante. Los procesos en segundo plano, como notificaciones o informes pesados, se pueden evaluar cuando exista una tarea que interrumpa el trabajo. [Arquitectura de InvenTree](https://docs.inventree.org/en/latest/develop/architecture/)
- **Contrato y persistencia.** Swagger UI permite inspeccionar las rutas en desarrollo. OpenAPI genera los tipos de React y las migraciones de EF Core versionan PostgreSQL. Las pruebas de dos cuentas comprueban la autorización y las políticas por fila.
- **Pruebas y distribución.** El repositorio conserva recorridos para ventas, existencias, correcciones y otros flujos. Android y Windows comparten la interfaz de React y tienen su empaquetado propio. Cada instalador requiere verificar los flujos en su dispositivo antes de publicarlo.
- **Operación y soporte.** Hay rutas separadas para comprobar API y base, errores con una referencia de diagnóstico y auditoría de correcciones. Una instalación compartida necesitará verificar red, dominio, respaldos periódicos y acceso a los registros técnicos.

## Mapa de carpetas

| Dónde | Qué se modifica ahí |
| --- | --- |
| `web/src/businesses`, `web/src/commerce` y módulos de rubro | Navegación, pantallas, formularios y presentación de cifras. |
| `web/src/ui` | Controles que reutilizan varias pantallas, como los campos y Revisar. |
| `web/src/api` | Cliente HTTP y contrato TypeScript generado desde OpenAPI. |
| `web/android` y `web/src-tauri` | Proyectos que empaquetan React para Android y Windows. |
| `server/KhypuPyme.Api/Commerce` | Rutas, comandos, consultas, transacciones y reglas de compras, ventas y Caja. |
| `server/KhypuPyme.Api/Timber`, `Retail` y `Services` | Reglas y consultas específicas de madera, comercio y servicios. |
| `server/KhypuPyme.Api/Persistence` | Modelo de EF Core y migraciones del esquema PostgreSQL. |
| `server/KhypuPyme.Api/Hosting` y `Security` | Configuración HTTP y tratamiento de errores. Confirmación de identidad y auditoría de las operaciones. |
| `scripts` | Arranque, migraciones y respaldos. Los archivos `Test-Block*.ps1` son entradas para las pruebas y llaman a recorridos `test-block*.mjs`. |
| `.github/workflows` | Comprobaciones que GitHub puede ejecutar en cada solicitud de cambios. |

## Dónde empezar según el cambio

- **Mover un campo o mejorar una pantalla.** Abre el componente `.tsx` del módulo en `web/src`. Si el campo pertenece a un control que aparece en varios módulos, revisa primero `web/src/ui`.
- **Cambiar una regla de venta.** Sigue [CommerceEndpoints.cs](../server/KhypuPyme.Api/Commerce/CommerceEndpoints.cs), [CommerceService.cs](../server/KhypuPyme.Api/Commerce/CommerceService.cs) y [TradeOperations.cs](../server/KhypuPyme.Api/Commerce/TradeOperations.cs). El servicio coordina autorización, resultado y confirmación. [CommerceTransaction.cs](../server/KhypuPyme.Api/Commerce/CommerceTransaction.cs) comparte el contexto y reúne operaciones como actualizar el efectivo y generar un número de comprobante.
- **Cambiar datos que viajan por la red.** Edita el contrato de C#, compila y genera de nuevo los tipos de React con `pnpm --dir web generate:api`. Guarda el cambio en el contrato y el esquema generado juntos.
- **Cambiar cómo se guarda un dato.** Revisa el modelo de EF Core y crea una migración. Comprueba primero su aplicación en la base de pruebas.
- **Cambiar permisos.** Revisa la autorización de la ruta, las consultas del negocio y las políticas por fila de PostgreSQL. Ejecuta los recorridos de dos cuentas con `Test-Block2.ps1` y de seguridad y correcciones con `Test-Block9.ps1`.

## Respaldo local cifrado

[Backup-Postgres.ps1](../scripts/Backup-Postgres.ps1) usa `pg_dump` y [age](https://github.com/FiloSottile/age#readme). El respaldo contiene el esquema `khypupyme` y sus datos. Para restaurar en otro equipo hay que configurar también los roles y credenciales del PostgreSQL de destino.

1. En PowerShell 7.4 o posterior, con `age.exe` y `age-keygen.exe` instalados, crea una identidad privada terminada en `.agekey` fuera del proyecto con `age-keygen -o ruta-privada.agekey`.
2. Obtén su clave pública con `age-keygen -y ruta-privada.agekey`. Ejecuta `./scripts/Backup-Postgres.ps1 -Recipient CLAVE_PUBLICA` desde el proyecto. Usa `-Database Test` para ensayar con la base de pruebas. `-Directory` permite usar otra carpeta para la copia cifrada.
3. Conserva la identidad privada aparte del respaldo. Para verificar la recuperación, descifra con `age -d -i ruta-privada.agekey -o temporal.dump copia.pgdump.age` en una carpeta privada, y restaura `temporal.dump` en una base vacía y aislada con `pg_restore --host 127.0.0.1 --port PUERTO_AISLADO --username USUARIO_RESTAURACION --single-transaction --no-owner --no-acl --dbname khypupyme2_restore temporal.dump`. Luego retira el archivo temporal descifrado.
4. Guarda también una copia del respaldo cifrado fuera del equipo. La identidad privada es necesaria para recuperar los datos si se pierde el equipo.

## Estado de la revisión

Las comprobaciones del repositorio cubren la compilación del servidor y frontend, tipos, lint e integridad del contrato. En Windows se pueden repetir los recorridos con PostgreSQL de pruebas, el buzón local y, para los flujos con interfaz, Edge. El workflow está preparado para ejecutarse cuando esté disponible en GitHub.

La revisión local del 27 de noviembre de 2026 confirmó CA-01 y CA-02 en Edge, el aislamiento entre cuentas y el flujo de correcciones. El respaldo cifrado de prueba se descifró y se restauraron 40 tablas en una instancia separada. Los análisis de NuGet y pnpm concluyeron sin avisos de vulnerabilidades y Gitleaks no encontró secretos en el historial local revisado ni en los cambios preparados para publicar.

El servidor aplica las cabeceras básicas también en desarrollo. HSTS se activa en el perfil de piloto y se debe comprobar con el dominio HTTPS real. La app sigue en localhost para el trabajo diario. El control antibot dedicado, la verificación de distribución en dispositivos físicos y la programación de copias periódicas pertenecen a sus respectivos pasos de publicación y operación.
