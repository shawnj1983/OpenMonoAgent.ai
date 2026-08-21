using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace OpenMono.Acp;

public static class MissionControlSetup
{
    public const string WebRootRelative = "MissionControl/wwwroot";

    public static string? ResolveWebRoot()
    {
        foreach (var root in CandidateWebRoots())
        {
            if (Directory.Exists(root))
                return root;
        }

        return null;
    }

    public static void Map(WebApplication app)
    {
        var webRoot = ResolveWebRoot();
        if (webRoot is null)
            return;

        app.MapGet("/", () => Serve(webRoot, "index.html", "text/html; charset=utf-8"));
        app.MapGet("/mission-control", () => Results.Redirect("/"));
        app.MapGet("/styles.css", () => Serve(webRoot, "styles.css", "text/css; charset=utf-8"));
        app.MapGet("/app.js", () => Serve(webRoot, "app.js", "application/javascript; charset=utf-8"));
    }

    private static IEnumerable<string> CandidateWebRoots()
    {
        yield return Path.Combine(AppContext.BaseDirectory, WebRootRelative);

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
        {
            yield return Path.Combine(dir, "MissionControl", "wwwroot");
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }
    }

    private static IResult Serve(string webRoot, string fileName, string contentType)
    {
        var path = Path.Combine(webRoot, fileName);
        if (!File.Exists(path))
            return Results.NotFound();

        return Results.File(path, contentType, enableRangeProcessing: false);
    }
}
