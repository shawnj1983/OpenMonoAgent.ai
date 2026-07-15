# API keys workspace

Local vault symlink + HTML doorway for provider consoles and key paths. **Never paste secrets into chat.**

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

## Files
| File | Purpose |
|------|---------|
| `.env` | Symlink → `~/jarvis/secrets/api-keys.env` (paste keys here locally) |
| `doorway.html` / `API Keys Door.html` | Workspace HTML hub |
| `.env.example` | Names only, no secrets |
