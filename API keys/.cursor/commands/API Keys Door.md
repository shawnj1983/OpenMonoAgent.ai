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
   That script prefers `/Volumes/Expansion/api-keys-doorway/index.html`, then falls back to `~/Projects/api-keys-doorway/index.html`, then this workspace's `doorway.html`. Optional: append `--vault` (or `-e`) to also open `~/jarvis/secrets/api-keys.env` in TextEdit. Never print vault contents.

2. Open the workspace doorway in the editor via `cursor-app-control` → `open_resource`:
   - Prefer `doorway.html` in this workspace
   - Also fine: `API Keys Door.html` (same hub)
   - Note: `open_resource` only opens files inside the agent workspace or under `~/.cursor` — use step 1 for Expansion/Projects paths.

3. Also open if reachable (skip any path that is missing / unmounted):
   - Workspace pointer to the Expansion hub: `API Keys Door (browser hub).html` via `open_resource`
   - If Expansion is mounted and the browser did not already open it, run:
     ```bash
     open /Volumes/Expansion/api-keys-doorway/index.html
     ```
   - Else if the Projects symlink exists:
     ```bash
     open "$HOME/Projects/api-keys-doorway/index.html"
     ```

4. Tell Shawn the click paths:
   - **Customize (agent catalog):** Left sidebar → **Customize** → **Commands** → **API Keys Door** (or type `/API Keys Door` in chat). This is a slash command — it does **not** embed the HTML page inside Customize.
   - **File explorer (actual HTML):** Left sidebar → **Explorer** → `API Keys Door.html` or `doorway.html`
   - **Browser:** the script in step 1 (Expansion when mounted; otherwise Projects / workspace fallback)
   - **Documents notes (optional):** `~/Documents/AI Keys/` — paste-notes folder, not the HTML doorway

## Rules

- Never print, paste, or echo secret values.
- Doorway = HTML hub (provider consoles + paths only).
- Vault = `~/jarvis/secrets/api-keys.env` (and workspace `.env` symlink) for pasting keys locally.
- Chat is never where keys go.
