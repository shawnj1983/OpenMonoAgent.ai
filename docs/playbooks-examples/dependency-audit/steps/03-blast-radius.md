# Step: Blast-Radius / Code-Dependency Analysis

You have, in `{{state.assess_deprecations}}`, a list of API symbols slated to
change for each upgrade candidate. Now determine whether **this codebase actually
depends on those symbols**, and how widely.

## Use the structural call graph (code-review-graph)

The auto-detected `code-review-graph` MCP server exposes structural tools as
`mcp__code-graph__*`. Load the ones you need first:

```text
ToolSearch  query: "select:mcp__code-graph__graph_callers,mcp__code-graph__graph_search,mcp__code-graph__graph_query"
```

For each slated-for-change symbol:

1. **Locate it** — `graph_search` (or `graph_query`) to confirm the symbol exists
   in the graph and get its canonical node.
2. **Find dependents** — `graph_callers` to list everything that calls/references
   it. Record each caller as `file.cs:line — CallingMethod`.
3. **Gauge depth** — if the symbol sits on a core path, follow callers one or two
   hops out (`graph_query`) to see how far a change propagates.

If `mcp__code-graph__*` tools are **not available** (graph not built), fall back to
`Search`/Glob for the symbol across the codebase, and explicitly label every verdict
in this step as **lower-confidence (graph unavailable)**.

## Translate findings into a verdict

For each upgrade candidate, classify each slated symbol:

- **Safe to upgrade** — zero callers in this codebase, or callers only in test
  scaffolding, or the changed signature is source-compatible.
- **Requires code changes** — N callers depend on the removed/changed symbol; list
  them and the concrete edit each needs.
- **Needs manual review** — high blast radius (many callers / core path), ambiguous
  migration, or graph-unavailable fallback.

Roll the per-symbol verdicts up to a single verdict per package upgrade.

Output, per candidate: the symbols checked, caller lists (`file:line`), the
per-symbol and rolled-up verdict, and the specific code changes required (if any).
State clearly whether the code graph was available or you fell back to text search.
