namespace OpenMono.Captain;

public sealed record CaptainRules
{
    public int Version { get; init; } = 1;

    /// <summary>
    /// Roots that the captain is allowed to scan, watch, move, and rename within.
    /// </summary>
    public List<string> Roots { get; init; } = [];

    /// <summary>
    /// Glob-like ignore patterns. These are applied as substring guards initially;
    /// we keep it simple and safe-by-default until patterns are upgraded.
    /// </summary>
    public List<string> Ignore { get; init; } = [];

    public CaptainOrganizationRules Organization { get; init; } = new();
}

public sealed record CaptainOrganizationRules
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// A path that acts as the \"inbox\" for new files (e.g. ~/Downloads). Events under this
    /// directory are eligible for auto move/rename.
    /// </summary>
    public string? InboxRoot { get; init; }

    /// <summary>
    /// Root folder where organized content is placed.
    /// </summary>
    public string? OrganizedRoot { get; init; }
}

