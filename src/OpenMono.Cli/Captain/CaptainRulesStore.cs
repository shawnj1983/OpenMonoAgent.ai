using OpenMono.Config;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenMono.Captain;

public static class CaptainRulesStore
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitDefaults)
        .Build();

    public static CaptainRules LoadOrDefault(AppConfig config)
    {
        var path = CaptainPaths.RulesPath(config);
        if (!File.Exists(path))
            return Default(config);

        var raw = File.ReadAllText(path);
        var rules = YamlDeserializer.Deserialize<CaptainRules>(raw);
        return Normalize(rules, config);
    }

    public static void Save(AppConfig config, CaptainRules rules)
    {
        var path = CaptainPaths.RulesPath(config);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var yaml = YamlSerializer.Serialize(rules);
        File.WriteAllText(path, yaml);
    }

    public static CaptainRules Default(AppConfig config)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Normalize(new CaptainRules
        {
            Version = 1,
            Roots = [config.WorkingDirectory],
            Ignore =
            [
                "/.git/",
                "/node_modules/",
                "/bin/",
                "/obj/",
                "/.openmono/",
            ],
            Organization = new CaptainOrganizationRules
            {
                Enabled = true,
                InboxRoot = Path.Combine(home, "Downloads"),
                OrganizedRoot = Path.Combine(home, "Organized"),
            }
        }, config);
    }

    private static CaptainRules Normalize(CaptainRules rules, AppConfig config)
    {
        var normalizedRoots = rules.Roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.GetFullPath(ExpandHome(r.Trim()), config.WorkingDirectory))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedRoots.Count == 0)
            normalizedRoots.Add(Path.GetFullPath(config.WorkingDirectory));

        var ignore = rules.Ignore
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Select(i => i.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var org = rules.Organization ?? new CaptainOrganizationRules();
        var inbox = string.IsNullOrWhiteSpace(org.InboxRoot) ? null : Path.GetFullPath(ExpandHome(org.InboxRoot.Trim()), config.WorkingDirectory);
        var organized = string.IsNullOrWhiteSpace(org.OrganizedRoot) ? null : Path.GetFullPath(ExpandHome(org.OrganizedRoot.Trim()), config.WorkingDirectory);

        return rules with
        {
            Roots = normalizedRoots,
            Ignore = ignore,
            Organization = org with { InboxRoot = inbox, OrganizedRoot = organized },
        };
    }

    private static string ExpandHome(string path)
    {
        if (!path.StartsWith('~'))
            return path;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path == "~") return home;
        if (path.StartsWith("~/") || path.StartsWith("~\\"))
            return Path.Combine(home, path[2..]);
        return path;
    }
}

