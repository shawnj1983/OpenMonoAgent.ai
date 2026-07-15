---
description: Open the API Keys Doorway (HTML hub + vault paths). Never paste secrets into chat.
---

# API Keys Door

Open Shawn's API keys doorway immediately. Do not ask for confirmation.

## Do this now (in order)

1. Run in shell (opens the HTML hub in the default browser):
   ```bash
   ~/jarvis/scripts/open-api-keys-doorway.sh
   ```
2. Open the workspace doorway in the editor via `cursor-app-control` → `open_resource`:
   - URI: the workspace file `doorway.html` (or `API Keys Door.html` if present)
3. Also open if reachable:
   - `file:///Volumes/Expansion/api-keys-doorway/index.html`
4. Tell Shawn the click paths:
   - **Customize (agent catalog):** Left sidebar → **Customize** → **Commands** → **API Keys Door** (or type `/API Keys Door` in chat). This is a slash command — it does **not** embed the HTML page inside Customize.
   - **File explorer (actual HTML):** Left sidebar → **Explorer** (files) → `API Keys Door.html` or `doorway.html` → open it.
   - **Browser:** the script above, or open `/Volumes/Expansion/api-keys-doorway/index.html`

## Rules

- Never print, paste, or echo secret values.
- Doorway = HTML hub (provider consoles + paths only).
- Vault = `~/jarvis/secrets/api-keys.env` (and workspace `.env` symlink) for pasting keys locally.
- Chat is never where keys go.
