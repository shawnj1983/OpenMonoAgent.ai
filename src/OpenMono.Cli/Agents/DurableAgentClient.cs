using OpenMono.Config;

namespace OpenMono.Agents;

/// <summary>
/// Placeholder for integrating durable execution backends for sub-agents and workflows.
/// Primary OSS recommendations (per research for "open sourced" agents-sdk equiv):
/// - Dapr Agents (CNCF Python, durable workflows, state, scheduling, MCP) — run sidecar, call via http/grpc.
/// - Temporal (with AntGent or durable-agents wrappers) — plan/execute loops as activities.
/// - Conductor / DuraGraph / Helix for alternatives.
///
/// CF Agents SDK is opt-in for Cloudflare Workers deployment (state, @callable, schedule, MCP, workflows).
/// 
/// Usage from OpenMono: if config.DurableAgents.Enabled, route sub-agent spawns / playbook steps here
/// for resumable, crash-proof "genius" thick analysis that survives restarts.
/// </summary>
public sealed class DurableAgentClient
{
    private readonly DurableConfig _cfg;

    public DurableAgentClient(DurableConfig cfg) => _cfg = cfg;

    public bool IsEnabled => _cfg.Enabled;

    public string Backend => _cfg.Backend;

    // Example stubs (implement with SDKs as needed):
    public Task<string> StartDurableSubAgentAsync(string agentType, string prompt, CancellationToken ct = default)
    {
        // e.g. for Dapr: POST to workflow, for Temporal: start Workflow etc.
        return Task.FromResult($"[Durable:{Backend}] Started durable execution for {agentType}. TaskId=stub");
    }

    // CF example (if using agents-sdk on Workers):
    // Connect via WebSocket or http to /agents/genius/xxx and use @callable RPC.
}