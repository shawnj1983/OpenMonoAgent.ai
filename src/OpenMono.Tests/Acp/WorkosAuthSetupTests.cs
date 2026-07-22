using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenMono.Acp;
using OpenMono.Config;
using Xunit;

namespace OpenMono.Tests.Acp;

public sealed class WorkosAuthSetupTests : IAsyncLifetime
{
    private readonly string _tempDir = Path.Combine(
        Path.GetTempPath(),
        "openmono-workos-auth-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly CancellationTokenSource _cts = new();
    private string? _origWorkspaceEnv;
    private string? _origAgentIdEnv;
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private int _port;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_tempDir);
        _port = GetFreePort();

        _origWorkspaceEnv = Environment.GetEnvironmentVariable("HOST_WORKSPACE_PATH");
        _origAgentIdEnv = Environment.GetEnvironmentVariable("ACP_AGENT_ID");
        Environment.SetEnvironmentVariable("HOST_WORKSPACE_PATH", _tempDir);
        Environment.SetEnvironmentVariable("ACP_AGENT_ID", "agt_workos_test");

        var settings = new AcpServerSettings
        {
            Port = _port,
            SessionTtlHours = 1,
            SessionsDirectory = Path.Combine(_tempDir, "acp-sessions"),
            Auth = new WorkosAuthSettings
            {
                Enabled = true,
                ApiKey = "sk_test_" + new string('a', 40),
                ClientId = "client_test",
                CookiePassword = new string('x', 32),
            },
        };

        var services = new ServiceCollection();
        services.AddSingleton(new AppConfig { DataDirectory = _tempDir });
        services.AddSingleton(settings);
        services.AddSingleton(sp => new AcpSessionStore(
            sp.GetRequiredService<AppConfig>(),
            sp.GetRequiredService<AcpServerSettings>(),
            startReaper: false));
        services.AddSingleton(new AcpLockFileWriter(settings, _tempDir));

        _app = AcpServer.Build(settings, services);
        await _app.StartAsync(_cts.Token);
        _client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri($"http://localhost:{_port}"),
        };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _cts.CancelAsync();
        try { await _app.StopAsync(); } catch { }
        await _app.DisposeAsync();

        Environment.SetEnvironmentVariable("HOST_WORKSPACE_PATH", _origWorkspaceEnv);
        Environment.SetEnvironmentVariable("ACP_AGENT_ID", _origAgentIdEnv);

        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Protected_api_returns_401_without_session_cookie()
    {
        var res = await _client.GetAsync("/api/v1/sessions");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var body = await res.Content.ReadFromJsonAsync<AuthError>();
        body!.error.Should().Be("authentication_required");
        body.login_url.Should().Be("/auth/login");
    }

    [Fact]
    public async Task Discovery_reports_auth_enabled_without_session()
    {
        var res = await _client.GetAsync("/api/v1/discovery");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await res.Content.ReadFromJsonAsync<DiscoveryResponse>();
        json!.auth!.enabled.Should().BeTrue();
        json.auth.login_url.Should().Be("/auth/login");
    }

    [Fact]
    public async Task Auth_login_redirects_to_workos()
    {
        var res = await _client.GetAsync("/auth/login");
        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().StartWith("https://");
    }

    private static int GetFreePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class AuthError
    {
        public string? error { get; set; }
        public string? login_url { get; set; }
    }

    private sealed class DiscoveryResponse
    {
        public AuthInfo? auth { get; set; }
    }

    private sealed class AuthInfo
    {
        public bool enabled { get; set; }
        public string? login_url { get; set; }
    }
}
