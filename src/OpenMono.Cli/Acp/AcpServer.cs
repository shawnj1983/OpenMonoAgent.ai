using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace OpenMono.Acp;

public static class AcpServer
{
    public static WebApplication Build(AcpServerSettings settings, IServiceCollection services)
    {
        var builder = WebApplication.CreateBuilder();

        foreach (var d in services)
            builder.Services.Add(d);

        builder.WebHost.ConfigureKestrel(o =>
        {
            if (settings.BindAllInterfaces) o.ListenAnyIP(settings.Port);
            else o.ListenLocalhost(settings.Port);
        });

        builder.Services.AddSingleton(settings);

        var auth = ResolveAuthSettings(settings);
        settings.Auth = auth;
        if (auth.IsConfigured)
            WorkosAuthSetup.Register(builder, auth);

        var app = builder.Build();

        if (auth.IsConfigured)
            WorkosAuthSetup.Use(app);

        AcpEndpoints.Map(app);
        MissionControlSetup.Map(app);
        return app;
    }

    private static WorkosAuthSettings ResolveAuthSettings(AcpServerSettings settings)
    {
        var auth = settings.Auth ?? new WorkosAuthSettings();
        auth.ApiKey ??= Environment.GetEnvironmentVariable("WORKOS_API_KEY");
        auth.ClientId ??= Environment.GetEnvironmentVariable("WORKOS_CLIENT_ID");
        auth.CookiePassword ??= Environment.GetEnvironmentVariable("WORKOS_COOKIE_PASSWORD");

        if (auth.RedirectUri.Contains("{port}", StringComparison.Ordinal))
            auth.RedirectUri = auth.RedirectUri.Replace("{port}", settings.Port.ToString(), StringComparison.Ordinal);

        return auth;
    }
}
