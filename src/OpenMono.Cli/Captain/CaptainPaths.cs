using OpenMono.Config;

namespace OpenMono.Captain;

public static class CaptainPaths
{
    public static string CaptainDir(AppConfig config) =>
        Path.Combine(config.DataDirectory, "captain");

    public static string RulesPath(AppConfig config) =>
        Path.Combine(CaptainDir(config), "rules.yml");

    public static string DbPath(AppConfig config) =>
        Path.Combine(CaptainDir(config), "captain.db");

    public static string ActionsJournalPath(AppConfig config) =>
        Path.Combine(CaptainDir(config), "actions.jsonl");

    public static string QueuePath(AppConfig config) =>
        Path.Combine(CaptainDir(config), "queue.jsonl");

    public static string QueueCursorPath(AppConfig config) =>
        Path.Combine(CaptainDir(config), "queue.cursor");

    public static string PidPath(AppConfig config) =>
        Path.Combine(CaptainDir(config), "captain.pid");

    public static string LogPath(AppConfig config) =>
        Path.Combine(CaptainDir(config), "captain.log");
}

