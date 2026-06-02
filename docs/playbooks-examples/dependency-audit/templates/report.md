# Dependency Health Report

> A plain-language summary of the third-party software packages this project
> depends on — which ones are risky, what it means, and what to do about it.

- **Project:** `{{params.target}}`
- **Date:** {{env.DATE}}
- **Branch:** {{env.GIT_BRANCH}}

---

## At a glance

| Category | Count | What it means |
| --- | --- | --- |
| 🔴 Security risks | _N_ | Packages with known vulnerabilities — fix first |
| 🟠 Outdated / discontinued | _N_ | Packages no longer maintained or replaced by something newer |
| 🟡 Simply behind | _N_ | Packages that just have a newer version available |
| ✅ Healthy | _N_ | Nothing to do |

**Overall health:** 🔴 Needs attention / 🟠 Some risk / ✅ Healthy

> Two or three sentences, no jargon: what is the overall situation, what is the
> single most important thing to do, and roughly how much effort it takes.

---

## What you should do (plain English)

A short, numbered to-do list anyone can follow. One line each, no technical terms.

1. **Update `<Package>` now** — it has a known security hole. _Low effort, safe to do._
2. **Plan to replace `<Package>`** — the makers stopped supporting it. _Needs a developer; medium effort._
3. **Optional: update `<Package>`** — newer version available, no urgency.

> If something can't be fixed easily, say so in plain terms and explain why
> (e.g. "updating this would require rewriting part of the code, so plan it as a
> separate task").

---

## The details (for developers)

Everything below is the technical backing for the summary above. A
non-technical reader can stop here.

### Security risks (vulnerable packages)

For each vulnerable package:

#### `<Package>` `<current>` → `<fixed>`

- **Severity:** Critical / High / Moderate / Low
- **Advisory:** GHSA / CVE id + link
- **Type:** Direct / Transitive (and the direct parent if transitive)
- **Affected projects:** list of `.csproj`
- **Recommended action:** bump to `<version>` / replace / no fix available

### Discontinued or deprecated packages

#### `<Package>` — deprecated

- **Reason:** Legacy / Critical bugs / Other (NuGet deprecation reason)
- **Successor:** recommended replacement package, if any
- **Deprecated APIs in use:** the specific types/methods this codebase calls
  that are slated for removal or replacement (from documentation review)
- **Migration notes:** link to upgrade/migration guide + key breaking changes

### Will an update break our code? (blast-radius analysis)

For each upgrade candidate that touches a deprecated or signature-changed API,
report what the **code-review-graph** structural analysis found:

#### `<Package>` upgrade → impacts `<Symbol>`

- **Symbol slated for change:** `Namespace.Type.Method`
- **Callers found:** _N_ (via `graph_callers`)
  - `file.cs:line` — `CallingMethod`
  - ...
- **Blast radius:** Low (≤2 callers, leaf) / Medium / High (core path, many callers)
- **Verdict:** Safe to upgrade / Requires code changes / Needs manual review
- **Required code changes:** concrete edits, if any

### Prioritized action plan (engineering)

Ordered security-first, then deprecations, then version currency:

1. **[Critical]** `<Package>` `<from>` → `<to>` — _why, and any code changes required_
2. **[High]** ...
3. **[Deprecation]** ...
4. **[Maintenance]** ...

#### Blocked / deferred

- `<Package>` — _blocked because `<reason>` (e.g. high blast radius, breaking API, no fix yet)_

---

## Appendix — raw scan output

Reference to `.audit-out/{vulnerable,deprecated,outdated}.{json,txt}`.
