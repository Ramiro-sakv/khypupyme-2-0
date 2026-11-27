namespace KhypuPyme.Api.Hosting;

internal static class RequestSecurity
{
    public static void UseRequestSecurity(this WebApplication app, PilotOptions pilot, bool renderProxy)
    {
        if (renderProxy) app.UseForwardedHeaders();

        app.Use(async (context, next) =>
        {
            // Aplica también a archivos estáticos y a respuestas de error.
            context.Response.OnStarting(static state =>
            {
                var headers = ((HttpContext)state).Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = "no-referrer";
                // Impide que otro sitio coloque la app en un marco y oculte sus controles.
                headers["Content-Security-Policy"] = "frame-ancestors 'none'";
                headers.XFrameOptions = "DENY";
                return Task.CompletedTask;
            }, context);

            if (pilot.Enabled)
            {
                var isDatabaseProbe = HttpMethods.IsGet(context.Request.Method) &&
                    context.Request.Path == "/api/v1/health/database";
                if (!isDatabaseProbe && (!context.Request.IsHttps ||
                    !string.Equals(context.Request.Host.Host, pilot.Origin.Host, StringComparison.OrdinalIgnoreCase)))
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }
            }

            await next();
        });

        // El piloto exige HTTPS. El acceso de desarrollo sigue usando la interfaz de loopback.
        if (pilot.Enabled) app.UseHsts();
    }
}
