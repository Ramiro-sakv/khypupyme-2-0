using System.Text.Json.Serialization;
using KhypuPyme.Api.Accounts;
using KhypuPyme.Api.Businesses;
using KhypuPyme.Api.Billing;
using KhypuPyme.Api.Commerce;
using KhypuPyme.Api.Health;
using KhypuPyme.Api.Persistence;
using KhypuPyme.Api.Timber;
using KhypuPyme.Api.Retail;
using KhypuPyme.Api.Security;
using KhypuPyme.Api.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

// Este archivo arranca la API. Estas opciones permiten realizar una tarea puntual de instalación y terminar antes de abrir el servidor HTTP.
var provisionClients = args.Contains("--register-native-clients", StringComparer.Ordinal);
var migrate = args.Contains("--migrate-pilot", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(provisionClients || migrate ? [] : args);
// Las opciones de ejecución reúnen el entorno y la configuración. El perfil de piloto aplica controles extra cuando está habilitado.
var pilot = new PilotOptions(builder.Configuration, builder.Environment);
if (pilot.Enabled) builder.Configuration["AllowedHosts"] = pilot.Origin.Host + ";localhost;127.0.0.1";
builder.Services.AddSingleton(pilot);
// Registra la presentación común de los errores HTTP. SafeErrors controla qué detalles se pueden devolver al navegador.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SafeErrors>();
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
if (pilot.Enabled) builder.Logging.AddFilter("OpenIddict", LogLevel.Error);
builder.Services.AddHttpClient("pilot-mail", client => client.Timeout = TimeSpan.FromSeconds(15))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

// La API obtiene de su configuración los datos para conectarse a PostgreSQL. Npgsql es el proveedor que EF Core usa para hablar con ese motor.
var connectionString = builder.Configuration.GetConnectionString("KhypuPyme");
if (migrate) PilotOptions.RequireDatabase(connectionString, application: false);
builder.Services.AddDbContext<KhypuPymeDbContext>(options => options.UseNpgsql(connectionString));
builder.AddAccounts(pilot);
// La política CORS indica qué orígenes de los clientes instalados pueden llamar a esta API desde su interfaz web.
builder.Services.AddCors(options => options.AddPolicy("native", rule => rule
    .WithOrigins("https://localhost", "https://tauri.localhost")
    .WithMethods("GET", "POST", "PUT").WithHeaders("Authorization", "Content-Type")));
var renderProxy = pilot.Enabled && builder.Configuration.GetValue<bool>("Pilot:UseRenderProxy");
if (renderProxy)
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
    });
// Estos servicios se crean por ámbito de solicitud. Los que consultan o escriben datos pueden recibir el mismo DbContext durante esa petición.
builder.Services.AddScoped<BusinessAccess>();
builder.Services.AddScoped<LicenseAccess>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<CommerceService>();
builder.Services.AddScoped<CommerceReads>();
builder.Services.AddScoped<StoreReads>();
builder.Services.AddScoped<StockReads>();
builder.Services.AddScoped<TimberPracticeService>();
builder.Services.AddScoped<SecurityProofs>();
builder.Services.AddScoped<BusinessRemoval>();
builder.Services.AddScoped<AccountActions>();

// Una vez registradas las dependencias, se construye la aplicación y se decide si realiza una tarea puntual o atiende peticiones.
var app = builder.Build();
if (migrate)
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<KhypuPymeDbContext>().Database.MigrateAsync();
    return;
}
if (provisionClients)
{
    if (!pilot.Native) throw new InvalidOperationException("Habilita el perfil de piloto o desarrollo.");
    await NativeAuthorization.RegisterClientsAsync(app.Services, app.Configuration);
    return;
}

app.UseRequestSecurity(pilot, renderProxy);
if (pilot.Enabled) await NativeAuthorization.RegisterClientsAsync(app.Services, app.Configuration);
// La canalización procesa cada petición antes de ejecutar su ruta. El orden importa, porque unas etapas necesitan el resultado de otras.
app.UseExceptionHandler();
if (app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists) { app.UseDefaultFiles(); app.UseStaticFiles(); }
app.UseRouting();
app.UseCors("native");
// Autenticación identifica a la cuenta. Después, autorización decide si puede usar la ruta y el servicio valida el negocio concreto.
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    // En desarrollo, OpenAPI describe las rutas y Swagger UI permite explorarlas desde el navegador.
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "API de KhypuPyme 2.0";
        options.SwaggerEndpoint("/openapi/v1.json", "KhypuPyme 2.0 · API v1");
        options.EnableFilter();
    });
}

// Cada módulo registra aquí sus rutas. Por ejemplo, MapCommerce expone las consultas de stock y los comandos de venta.
app.MapGroup("/api/v1/health").WithTags("Health").MapHealthEndpoints();
app.MapAccounts();
app.MapBusinesses();
app.MapBilling();
app.MapCommerce();
app.MapSecurity();
app.MapTimberPractice();
app.MapNativeAuthorization();
// Inicia la escucha HTTP y mantiene la API atendiendo solicitudes hasta que se detenga el proceso.
app.Run();
