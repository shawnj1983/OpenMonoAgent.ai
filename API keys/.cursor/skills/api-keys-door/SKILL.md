---
name: api-keys-door
description: >-
  Opens the API Keys Doorway HTML hub and vault paths for Shawn. Use when he
  asks for API Keys Door, doorway, Customize API keys, or to open the keys hub.
  Never paste or print secret values.
disable-model-invocation: false
---

# API Keys Door

## Honest placement

Cursor **Customize** is an agent-extension catalog (plugins, skills, MCPs, rules, commands, hooks). It cannot host arbitrary HTML links or webviews. This skill appears under **Customize → Skills** as `api-keys-door`. The slash command **API Keys Door** appears under **Customize → Commands**.

The actual HTML doorway lives in the **file Explorer** and the browser — not as a clickable HTML tile under Customize.

## When invoked

1. Run `~/jarvis/scripts/open-api-keys-doorway.sh` (browser).
2. Open workspace `doorway.html` / `API Keys Door.html` in the editor (`open_resource`).
3. Prefer Expansion hub when mounted: `/Volumes/Expansion/api-keys-doorway/index.html`.
4. Confirm click path to Shawn in plain text.

## Paths

| What | Where |
|------|--------|
| Browser hub | `/Volumes/Expansion/api-keys-doorway/index.html` |
| Open script | `~/jarvis/scripts/open-api-keys-doorway.sh` |
| Workspace HTML | `API keys/doorway.html` (also `API Keys Door.html`) |
| Vault | `~/jarvis/secrets/api-keys.env` |
| Customize command | Left → Customize → Commands → **API Keys Door** |
| Customize skill | Left → Customize → Skills → **api-keys-door** |
| Chat invoke | `/API Keys Door` or `/api-keys-door` |
