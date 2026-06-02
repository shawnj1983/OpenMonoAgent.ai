# Step: Triage

You have the raw scan output in `{{state.scan_packages}}` and the artifacts under
`.audit-out/` (`vulnerable.json/txt`, `deprecated.json/txt`, `outdated.json/txt`).
Read the JSON artifacts if present (richer than text); otherwise parse the text.

Build a single normalized inventory. For every flagged package record:

- **Package id** and **resolved version**
- **Category**: vulnerable / deprecated / outdated (a package may be more than one)
- **Direct or transitive** — if transitive, the top-level package that pulls it in
- **Affected project(s)** — which `.csproj`
- For vulnerable: **severity** and **advisory id + URL** exactly as reported
- For deprecated: **deprecation reason(s)** and **suggested alternative** if NuGet provides one
- For outdated: **current → latest** (and latest-stable if different)

Then select the **upgrade candidates** — the packages worth deeper analysis in the
next steps. Include:

- every vulnerable package at or above the `{{params.severity}}` threshold, and
- every deprecated package, and
- any outdated package whose jump is a **major** version (likely breaking).

Skip patch/minor-only outdated packages from deep analysis (note them for the
maintenance section, but they don't need blast-radius work).

Output a compact, structured summary: the full inventory table, then a clearly
labeled list of upgrade candidates with the specific reason each was selected.
Do not fetch documentation yet and do not query the code graph yet.
