using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using WorkOS;

namespace OpenMono.Acp;

public static class WorkosAuthSetup
{
    private const string OAuthStateKey = "workos_oauth_state";

    public static void Register(WebApplicationBuilder builder, WorkosAuthSettings auth)
    {
        var client = new WorkOSClient(new WorkOSOptions
        {
            ApiKey = auth.ApiKey!,
            ClientId = auth.ClientId!,
        });

        builder.Services.AddSingleton(auth);
        builder.Services.AddSingleton(client);
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.Cookie.Name = "openmono.workos.session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
        });
    }

    public static void Use(WebApplication app)
    {
        var auth = app.Services.GetRequiredService<WorkosAuthSettings>();
        if (!auth.IsConfigured)
            return;

        app.UseSession();
        app.Use(RequireApiAuth);
        MapAuthRoutes(app, auth);
    }

    public static void MapAuthRoutes(WebApplication app, WorkosAuthSettings auth)
    {
        var client = app.Services.GetRequiredService<WorkOSClient>();

        app.MapGet("/auth/login", (HttpContext ctx) =>
        {
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            ctx.Session.SetString(OAuthStateKey, state);

            var url = client.UserManagement.GetAuthorizationUrl(new UserManagementGetAuthorizationUrlOptions
            {
                RedirectUri = auth.RedirectUri,
                Provider = UserManagementAuthenticationProvider.Authkit,
                State = state,
            });

            return Results.Redirect(url);
        });

        app.MapGet("/auth/callback", async (HttpContext ctx, string? code, string? state) =>
        {
            if (string.IsNullOrWhiteSpace(code))
                return Results.BadRequest(new { error = "missing_code" });

            var expectedState = ctx.Session.GetString(OAuthStateKey);
            ctx.Session.Remove(OAuthStateKey);
            if (string.IsNullOrWhiteSpace(expectedState) || !string.Equals(expectedState, state, StringComparison.Ordinal))
                return Results.BadRequest(new { error = "invalid_state" });

            var authResponse = await client.UserManagement.AuthenticateWithCodeAsync(
                new AuthenticateWithCodeOptions { Code = code });

            var sealedSession = SessionService.SealSessionFromAuthResponse(
                authResponse.AccessToken,
                authResponse.RefreshToken,
                auth.CookiePassword!);

            ctx.Response.Cookies.Append(auth.SessionCookieName, sealedSession, new CookieOptions
            {
                HttpOnly = true,
                Secure = ctx.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            });

            return Results.Redirect("/");
        });

        app.MapGet("/auth/logout", async (HttpContext ctx) =>
        {
            if (ctx.Request.Cookies.TryGetValue(auth.SessionCookieName, out var cookie) && !string.IsNullOrWhiteSpace(cookie))
            {
                try
                {
                    var session = await client.Session.AuthenticateAsync(cookie, auth.CookiePassword!);
                    if (session.Authenticated && !string.IsNullOrWhiteSpace(session.SessionId))
                    {
                        var logoutUrl = client.UserManagement.GetLogoutUrl(new UserManagementGetLogoutUrlOptions
                        {
                            SessionId = session.SessionId,
                            ReturnTo = $"{ctx.Request.Scheme}://{ctx.Request.Host}/",
                        });

                        ctx.Response.Cookies.Delete(auth.SessionCookieName, new CookieOptions { Path = "/" });
                        return Results.Redirect(logoutUrl);
                    }
                }
                catch
                {
                    // Fall through to local cookie clear.
                }
            }

            ctx.Response.Cookies.Delete(auth.SessionCookieName, new CookieOptions { Path = "/" });
            return Results.Redirect("/");
        });

        app.MapGet("/auth/me", async (HttpContext ctx) =>
        {
            var session = await AuthenticateRequest(client, auth, ctx);
            if (session is null)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                authenticated = true,
                session_id = session.SessionId,
                organization_id = session.OrganizationId,
                roles = session.Roles,
            });
        });
    }

    private static async Task RequireApiAuth(HttpContext ctx, RequestDelegate next)
    {
        var auth = ctx.RequestServices.GetRequiredService<WorkosAuthSettings>();
        if (!auth.IsConfigured)
        {
            await next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "";
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/v1/discovery", StringComparison.OrdinalIgnoreCase))
        {
            await next(ctx);
            return;
        }

        var client = ctx.RequestServices.GetRequiredService<WorkOSClient>();
        if (await AuthenticateRequest(client, auth, ctx) is not null)
        {
            await next(ctx);
            return;
        }

        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new
        {
            error = "authentication_required",
            login_url = "/auth/login",
        });
    }

    private static async Task<SessionAuthResult?> AuthenticateRequest(
        WorkOSClient client,
        WorkosAuthSettings auth,
        HttpContext ctx)
    {
        if (!ctx.Request.Cookies.TryGetValue(auth.SessionCookieName, out var cookie) || string.IsNullOrWhiteSpace(cookie))
            return null;

        try
        {
            var session = await client.Session.AuthenticateAsync(cookie, auth.CookiePassword!);
            return session.Authenticated ? session : null;
        }
        catch
        {
            return null;
        }
    }
}
