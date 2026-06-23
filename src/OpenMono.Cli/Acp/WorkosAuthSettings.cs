namespace OpenMono.Acp;

/// <summary>
/// Optional WorkOS AuthKit gate for Mission Control and the ACP HTTP API.
/// When enabled, unauthenticated requests to /api/v1/* receive 401.
/// </summary>
public sealed class WorkosAuthSettings
{
    public bool Enabled { get; set; }

    public string? ApiKey { get; set; }
    public string? ClientId { get; set; }

    /// <summary>32+ character secret used to seal the WorkOS session cookie.</summary>
    public string? CookiePassword { get; set; }

    /// <summary>OAuth redirect URI registered in the WorkOS dashboard.</summary>
    public string RedirectUri { get; set; } = "http://localhost:{port}/auth/callback";

    public string SessionCookieName { get; set; } = "wos-session";

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(CookiePassword)
        && CookiePassword.Length >= 32;
}
