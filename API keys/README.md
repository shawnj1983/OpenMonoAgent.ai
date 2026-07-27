# API keys workspace

Local vault symlink + HTML doorway for provider consoles and key paths. **Never paste secrets into chat.**

**Canonical secrets file (never commit):** `~/jarvis/secrets/api-keys.env`

Paste keys there. MCP servers launched via `~/jarvis/scripts/mcp-with-keys.sh` inherit every variable in that file.

## Where to find the door (Cursor)

### Customize (agent catalog — not an HTML page)
Cursor **Customize** cannot host arbitrary HTML links. Closest entries:

1. Left sidebar → **Customize** → **Commands** → **API Keys Door**
2. Or in Agent chat type: `/API Keys Door`
3. Also: **Customize** → **Skills** → **api-keys-door** (`/api-keys-door`)

### Explorer (actual HTML on the left file rail)
1. Left sidebar → **Explorer** (files icon / folder tree)
2. Open **`API Keys Door.html`** (symlink → `doorway.html`)
3. Optional: **`API Keys Door (browser hub).html`** → Expansion hub

### Browser
```bash
~/jarvis/scripts/open-api-keys-doorway.sh
```
Opens `/Volumes/Expansion/api-keys-doorway/index.html`.

## Adding or rotating a key (example: Tailscale)

1. Open https://login.tailscale.com/admin/settings/keys
2. Generate an **API access token** (`tskey-api-...`) — not an auth key (`tskey-auth-...`); those are for joining devices, not for API calls.
3. Paste into `~/jarvis/secrets/api-keys.env`:

```
TAILSCALE_API_KEY=tskey-api-...
TAILSCALE_TAILNET=-
```

4. Tell Alfred: **keys are in**

Tailscale MCP is already wired in `~/.cursor/mcp.json` as `tailscale` (`@hexsleeves/tailscale-mcp-server`).

## Shared keys for "everybody"

Do **not** paste secrets into each MCP server block. Put them once in `api-keys.env`, then launch servers with:

```bash
~/jarvis/scripts/mcp-with-keys.sh <command> [args...]
```

That is how Tailscale is configured globally for Cursor.

## Files
| File | Purpose |
|------|---------|
| `.env` | Symlink → `~/jarvis/secrets/api-keys.env` (paste keys here locally) |
| `doorway.html` / `API Keys Door.html` | Workspace HTML hub |
| `.env.example` | Names only, no secrets |

## Voice input (no key required)
- **Wispr Flow** — AI dictation in any app. Press `fn` to speak.
  - Install: [wisprflow.ai/downloads](https://wisprflow.ai/downloads)
  - Direct: [Apple Silicon](https://dl.wisprflow.ai/mac-apple/latest) · [Intel](https://dl.wisprflow.ai/mac-intel/latest)
  - Current status: installed at `/Applications/Wispr Flow.app`
