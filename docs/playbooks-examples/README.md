# Playbook Examples

A gallery of ready-to-use [playbooks](../PLAYBOOKS.md) for OpenMono.ai. Each
subdirectory is a complete, self-contained playbook — a `PLAYBOOK.md` plus any
`steps/`, `scripts/`, and `templates/` it needs.

These are **examples to learn from and copy**, not installed playbooks. To use one,
copy its directory into a discovery location (see [Installing](#installing-an-example)).

---

## The examples

| Playbook | Trigger | What it does |
|----------|---------|--------------|
| [commit](commit/) | auto | Inspect staged changes, generate a conventional commit message, and commit. |
| [release](release/) | manual | End-to-end release pipeline — pre-flight, change analysis, changelog, version bump, test validation, git tag, optional Docker push. |
| [pr-ready](pr-ready/) | manual | Get a branch PR-ready — sync with target, run tests, lint, generate a PR description, open the pull request. |
| [db-migrate](db-migrate/) | manual | Run database migrations safely across environments — validate, dry-run, review diff, apply to staging, smoke test, then production. |
| [deploy-ftp](deploy-ftp/) | manual | Build, diff local output against the remote FTP server, and upload only changed files — pauses for review before transfer. |
| [incident-response](incident-response/) | manual | Structured incident response — gather logs, find blast radius, mitigate, verify recovery, auto-generate a postmortem. |
| [dependency-audit](dependency-audit/) | both | Scan .NET projects for vulnerable / deprecated / outdated NuGet packages, check breaking changes against the code graph, and write a prioritized markdown report. |
| [graphify](graphify/) | both | Query and manage the graphify knowledge graph for this codebase. |
| [file-scan](file-scan/) | manual | Minimal two-step example — create workspace files, then grep and report. Good starting point. |

---

## Anatomy of an example

```
<playbook-name>/
├── PLAYBOOK.md          # Required — YAML frontmatter (metadata, params, steps) + system prompt
├── steps/               # Optional — one markdown file per step
├── scripts/             # Optional — shell scripts a step invokes
└── templates/           # Optional — output format templates
```

The full format is documented in [docs/PLAYBOOKS.md](../PLAYBOOKS.md).

---

## Installing an example

Playbooks are discovered from these locations (highest priority first):

| Path | Scope |
|------|-------|
| `~/.openmono/playbooks/<name>/` | All projects for this user |
| `.openmono/playbooks/<name>/` | The current project only |

Copy an example into one of them:

```bash
# Project scope — available in this repo
cp -R docs/playbooks-examples/dependency-audit .openmono/playbooks/

# User scope — available everywhere
cp -R docs/playbooks-examples/dependency-audit ~/.openmono/playbooks/
```

Then invoke it by name:

```bash
/dependency-audit
/dependency-audit --target OpenMono.sln --severity high
```

---

## Gotchas worth knowing before you write your own

These trip up most first-time playbook authors (the example `PLAYBOOK.md` files
follow these rules):

- **Use `{{params.<name>}}`, not `{{parameters.<name>}}`.** The template engine only
  recognizes `params`.
- **Names can't contain hyphens.** Parameter names *and* step IDs are matched with a
  `\w+` regex, so `include-transitive` or `scan-packages` won't resolve. Use
  underscores: `transitive`, `scan_packages`.
- **Reference a prior step's output by its step ID:** `{{state.<step-id>}}`. Outputs
  are keyed by step ID — the `output:` field on a step is not used for this.
- **Gates fire *before* a step runs and skip it on decline.** Don't gate the step
  that produces your deliverable, or a declined gate will skip writing it.
- **Invoke scripts by absolute path:** `bash "{{playbook.base-path}}/scripts/foo.sh"`
  — relative paths resolve against the working directory, not the playbook directory.
- **Boolean parameters stringify to `"True"`/`"False"`** (capital). Compare
  case-insensitively in shell scripts.

---

## See also

- [docs/PLAYBOOKS.md](../PLAYBOOKS.md) — full PLAYBOOK.md format reference
- [docs/graphify.md](../graphify.md) — the semantic code graph used by `graphify`
- [docs/code-review-graph.md](../code-review-graph.md) — the structural call graph used by `dependency-audit`
