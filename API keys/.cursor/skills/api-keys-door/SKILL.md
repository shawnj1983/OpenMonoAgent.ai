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

1. Run `~/jarvis/scripts/open-api-keys-doorway.sh` (browser; Expansion → Projects → workspace fallback).
2. Open workspace `doorway.html` / `API Keys Door.html` in the editor (`open_resource`).
3. Also open `API Keys Door (browser hub).html` if present; use `open` for Expansion/Projects (outside `open_resource` scope).
4. Confirm click path to Shawn in plain text.

## Paths

| What | Where |
|------|--------|
| Browser hub | `/Volumes/Expansion/api-keys-doorway/index.html` |
| Projects fallback | `~/Projects/api-keys-doorway/index.html` |
| Open script | `~/jarvis/scripts/open-api-keys-doorway.sh` |
| Workspace HTML | `doorway.html` / `API Keys Door.html` |
| Expansion pointer | `API Keys Door (browser hub).html` |
| Documents notes | `~/Documents/AI Keys/` (paste notes, not the hub) |
| Voice input (no key) | Wispr Flow — `/Applications/Wispr Flow.app`; download at `https://wisprflow.ai/downloads` |
| Vault | `~/jarvis/secrets/api-keys.env` |
| Customize command | Left → Customize → Commands → **API Keys Door** |
| Customize skill | Left → Customize → Skills → **api-keys-door** |
| Chat invoke | `/API Keys Door` or `/api-keys-door` |
