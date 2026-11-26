using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Npgsql;

namespace KhypuPyme.Api.Hosting;

internal sealed record LocalNetworkHosting(IPAddress Address, int Port, IPNetwork Subnet)
{
    public static bool IsEnabled(IHostEnvironment environment) => environment.IsEnvironment("LocalNetwork");

    public static LocalNetworkHosting? Configure(WebApplicationBuilder builder, PilotOptions pilot)
    {
        if (!IsEnabled(builder.Environment)) return null;

        var config = builder.Configuration;
        var database = new NpgsqlConnectionStringBuilder(config.GetConnectionString("KhypuPyme"));
        if (pilot.Enabled || database.Host != "127.0.0.1" ||
            database.Database is not ("khypupyme2_dev" or "khypupyme2_test") ||
            database.Username != database.Database + "_app")
            throw new InvalidOperationException("La red local requiere una base PostgreSQL local y su usuario de aplicación.");

        var validation = database.Database == "khypupyme2_test";
        if (!IPAddress.TryParse(config["LocalNetwork:Address"], out var address) ||
            address.AddressFamily != AddressFamily.InterNetwork ||
            (validation && !address.Equals(IPAddress.Loopback)) ||
            !IPNetwork.TryParse(config["LocalNetwork:Subnet"], out var subnet) || !subnet.Contains(address) ||
            (!validation && !PrivateNetworks.Any(network =>
                network.Contains(subnet.BaseAddress) && subnet.PrefixLength >= network.PrefixLength)))
            throw new InvalidOperationException("Indica una dirección IPv4 privada y su subred local.");

        var port = config.GetValue<int>("LocalNetwork:Port");
        if (port is < 1024 or > 65535) throw new InvalidOperationException("El puerto HTTPS local no es válido.");

        var thumbprint = config["LocalNetwork:CertificateThumbprint"];
        if (string.IsNullOrWhiteSpace(thumbprint))
            throw new InvalidOperationException("Falta el certificado HTTPS de la red local.");
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var certificate = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false)
            .OfType<X509Certificate2>().SingleOrDefault();
        if (certificate is null || !certificate.HasPrivateKey ||
            DateTime.UtcNow < certificate.NotBefore.ToUniversalTime() || DateTime.UtcNow > certificate.NotAfter.ToUniversalTime())
            throw new InvalidOperationException("El certificado HTTPS local o su clave privada no están disponibles.");

        config["AllowedHosts"] = address.ToString();
        config["Google:ClientId"] = "";
        config["Google:ClientSecret"] = "";
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(address, port, endpoint => endpoint.UseHttps(certificate)));
        return new LocalNetworkHosting(address, port, subnet);
    }

    public void Use(WebApplication app)
    {
        if (!app.Environment.WebRootFileProvider.GetFileInfo("index.html").Exists)
            throw new InvalidOperationException("Falta la interfaz web compilada para la red local.");

        app.Use(async (context, next) =>
        {
            var remote = context.Connection.RemoteIpAddress;
            if (remote?.IsIPv4MappedToIPv6 == true) remote = remote.MapToIPv4();
            if (!context.Request.IsHttps || !Address.Equals(context.Connection.LocalIpAddress) ||
                context.Connection.LocalPort != Port || context.Request.Host.Port != Port ||
                !string.Equals(context.Request.Host.Host, Address.ToString(), StringComparison.Ordinal) ||
                remote is null || !Subnet.Contains(remote))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            await next();
        });
    }

    private static readonly IPNetwork[] PrivateNetworks =
        [IPNetwork.Parse("10.0.0.0/8"), IPNetwork.Parse("172.16.0.0/12"), IPNetwork.Parse("192.168.0.0/16")];
}
