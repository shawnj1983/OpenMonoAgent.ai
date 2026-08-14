#!/usr/bin/env bash
# Cloud Agent install step for OpenMono.
#
# Prepares the .NET-only development loop (build/test/format). The full product
# (llama.cpp inference server, Docker web services, model download) is NOT set
# up here — it is heavyweight and not needed for day-to-day code work.
#
# This script is idempotent: it can run repeatedly and against cached state.
set -euo pipefail

DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

# Read the SDK channel from global.json (e.g. "10.0.100" -> "10.0") so the
# installed SDK always matches what the repo pins.
CHANNEL="10.0"
if [[ -f "$REPO_DIR/global.json" ]]; then
    _ver="$(grep -oE '"version"[[:space:]]*:[[:space:]]*"[0-9]+\.[0-9]+' "$REPO_DIR/global.json" | grep -oE '[0-9]+\.[0-9]+' | head -1 || true)"
    [[ -n "$_ver" ]] && CHANNEL="$_ver"
fi

# ── .NET SDK ──────────────────────────────────────────────────────────────────
if command -v dotnet &>/dev/null && dotnet --list-sdks 2>/dev/null | grep -q "^${CHANNEL}\."; then
    echo "✓ .NET ${CHANNEL} SDK already installed ($(dotnet --version))"
elif [[ -x "$DOTNET_ROOT/dotnet" ]] && "$DOTNET_ROOT/dotnet" --list-sdks 2>/dev/null | grep -q "^${CHANNEL}\."; then
    echo "✓ .NET ${CHANNEL} SDK already installed ($("$DOTNET_ROOT/dotnet" --version))"
else
    echo "→ Installing .NET ${CHANNEL} SDK to $DOTNET_ROOT ..."
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel "$CHANNEL" --install-dir "$DOTNET_ROOT"
    rm -f /tmp/dotnet-install.sh
    echo "✓ .NET SDK installed ($("$DOTNET_ROOT/dotnet" --version))"
fi

# Make dotnet available in new interactive shells (guarded against duplicates).
if [[ -f "$HOME/.bashrc" ]] && ! grep -q 'DOTNET_ROOT' "$HOME/.bashrc"; then
    {
        echo ''
        echo '# .NET SDK (added by OpenMono Cloud Agent install)'
        echo 'export DOTNET_ROOT="$HOME/.dotnet"'
        echo 'export PATH="$DOTNET_ROOT:$PATH"'
    } >> "$HOME/.bashrc"
    echo "✓ Added .NET to PATH in ~/.bashrc"
fi

# ── Restore NuGet packages ──────────────────────────────────────────────────────
echo "→ Restoring NuGet packages ..."
"$DOTNET_ROOT/dotnet" restore "$REPO_DIR/OpenMono.sln"
echo "✓ Restore complete — development environment ready."
