# Step: Assess Deprecations & Breaking Changes

Working from the upgrade candidates in `{{state.triage}}`, research what each
upgrade actually entails. For each candidate:

1. **Find the authoritative source.** Use `WebSearch` then `WebFetch` to read the
   package's deprecation notice, migration/upgrade guide, release notes, or
   changelog for the version range `current → recommended`. Prefer official sources
   (the project's docs, GitHub releases, Microsoft Learn for `System.*` /
   `Microsoft.*` packages, the NuGet deprecation message itself).

2. **Extract the deprecated / changed surface.** Identify the concrete API symbols
   that are removed, renamed, or have changed signatures across the upgrade —
   e.g. `Namespace.Type.Method`, removed overloads, changed default behavior,
   dropped target frameworks. This is the key output: a precise list of symbols
   "slated for change" that the next step will check against the codebase.

3. **Capture the migration path.** For each breaking item, note the replacement API
   or the recommended code change, with the source URL.

Rules:
- Cite a URL for every breaking-change or deprecation claim. If you cannot find a
  source, say "no migration documentation located" and mark the item unverified —
  do not guess.
- If a deprecated package has no functional replacement (just "legacy"), note that
  the recommendation is removal or a successor library, not a version bump.

Output, per candidate package: the recommended target version, the bulleted list of
slated-for-change symbols (with source URLs), and the migration notes. Carry forward
the exact symbol names — the blast-radius step depends on them.
