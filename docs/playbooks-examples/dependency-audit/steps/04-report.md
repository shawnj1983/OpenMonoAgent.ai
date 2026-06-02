# Step: Write the Markdown Report

Synthesize everything into a single markdown report and write it to
`{{params.output}}`. Use the template at
`{{file:templates/report.md}}` as the structure — fill every placeholder with real
data from the prior steps; delete sections that have no findings rather than leaving
empty placeholders.

Sources to weave together:

- `{{state.triage}}` — normalized inventory + upgrade candidates
- `{{state.assess_deprecations}}` — slated-for-change symbols, migration notes, source URLs
- `{{state.blast_radius}}` — caller analysis and per-upgrade verdicts

## Write for two audiences

The report must be readable by a **non-technical reader** at the top and a
**developer** below. Keep the two tiers the template defines:

- **At a glance** + **What you should do (plain English)** — no jargon. Spell out
  what each problem means and the effort to fix it ("low effort, safe", "needs a
  developer"). Avoid CVE numbers, namespaces, and version strings in this tier;
  say "a known security hole" rather than "CVE-2024-…". Use the 🔴/🟠/🟡/✅ ratings.
- **The details (for developers)** — the full technical backing, where advisory
  IDs, symbols, callers, and version strings belong.

A normal user should be able to read only the top two sections and know exactly
what to do without understanding the technical sections.

Requirements for the report:

- **Summary table** with real counts and a 1-paragraph, jargon-free risk posture +
  single most urgent action.
- **Plain-English to-do list** — a numbered list anyone can act on, each item
  tagged with rough effort and whether it needs a developer.
- **Vulnerable / Deprecated** sections list only packages that appeared in the
  respective scan artifacts, each with its advisory/reason and cited source URL.
- **Blast-radius section** shows, per upgrade that touches called APIs, the symbol,
  the callers (`file.cs:line`), the blast-radius rating, and the verdict. Note
  explicitly whether the code-review-graph was used or a text-search fallback.
- **Action plan** ordered security-first: critical/high advisories → other
  advisories → deprecations → version-currency maintenance. Each item states the
  package, `from → to`, why, and any required code changes. List blocked/deferred
  upgrades separately with the blocking reason.
- **Appendix** points to `.audit-out/` artifacts.

Keep claims grounded — no invented CVEs, versions, or reasons; every advisory and
breaking-change line traces to a scan artifact or a cited URL.

**Write the report file.** This is the deliverable of the playbook — you MUST use
the WriteFile tool to write the completed markdown to `{{params.output}}`. Do not
ask the user whether to generate it, and do not stop at a summary in chat: write the
file. After writing, confirm the exact path and give a one-line summary of the top
finding so the user knows what to open.
