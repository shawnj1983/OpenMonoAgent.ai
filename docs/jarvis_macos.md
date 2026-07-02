## OpenMono "Jarvis" on macOS (always-on)

This guide turns OpenMono into an always-on local agent you can message from other programs (schedulers, voice frontends, scripts) via the ACP server.

### 1) Start the always-on agent server

Run:

- `openmono --acp-only`

By default it listens on `http://127.0.0.1:7475`.

### 2) Send it messages from your terminal (ACP client)

Create a session:

- `openmono acp new-session`

Then send a message:

- `openmono acp send <session_id> "Jarvis, how's it going?"`

If the agent pauses for permission or user input, the command exits non-zero and prints a pause payload you can respond to:

- `openmono acp respond-permission <session_id> <perm_id> allow`
- `openmono acp respond-input <session_id> <ask_id> "my answer"`

### 3) Enable macOS control (menus/keystrokes)

OpenMono includes a `MacAutomation` tool that uses AppleScript (`osascript`) for:

- opening/activating/quitting apps
- clicking menu items
- sending keystrokes / key codes

For menu-clicks and keystrokes, macOS will require **Accessibility** permissions for the process that runs OpenMono.

Examples the agent can perform (when allowed):

- Activate Chrome
- Click `File → Print…`
- Press Escape (`key code 53`)

### 4) Captain roots and "no delete" safety

Captain file operations remain restricted to `~/.openmono/captain/rules.yml` roots, and never delete files (only move/rename).

### API keys (do not paste into chat)

Do not paste API keys into OpenMono chat. Set them as environment variables instead:

- `OPENAI_API_KEY` for OpenAI (used by the `openai` provider).
- `ANTHROPIC_API_KEY` for Anthropic.
- `OPENMONO_API_KEY` for OpenMono’s OpenAI-compatible client config.

OpenMono redacts secret-like tokens from session history, but you should still rotate keys if you accidentally disclose them.

