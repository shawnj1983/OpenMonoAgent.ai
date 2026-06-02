#!/usr/bin/env bash
# scan-packages.sh — scans .NET projects for vulnerable, deprecated, and outdated
# NuGet packages and emits a machine-readable JSON block plus a human summary.
#
# Env overrides:
#   AUDIT_TARGET        — path to .sln/.csproj or a directory (default: .)
#   AUDIT_TRANSITIVE    — "true" to include transitive packages (default: true)
#   AUDIT_OUT_DIR       — where to write the raw JSON artifacts (default: ./.audit-out)
set -uo pipefail

TARGET="${AUDIT_TARGET:-.}"
TRANSITIVE="${AUDIT_TRANSITIVE:-true}"
OUT_DIR="${AUDIT_OUT_DIR:-./.audit-out}"

mkdir -p "$OUT_DIR"

echo "=== NuGet Dependency Audit ==="
echo "Target      : $TARGET"
echo "Transitive  : $TRANSITIVE"
echo "Artifacts   : $OUT_DIR"
echo ""

# --- pre-flight ---------------------------------------------------------------
if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: dotnet SDK not found in PATH. Install the .NET SDK and retry." >&2
    exit 1
fi

echo "--- dotnet SDK ---"
dotnet --version
echo ""

# Restore is required before package listing can resolve versions reliably.
echo "--- restore ---"
if ! dotnet restore "$TARGET" 2>&1 | tail -n 5; then
    echo "WARN: restore reported issues — results may be incomplete." >&2
fi
echo ""

# Treat anything that isn't an explicit false/no/0 as "include transitive".
# (A C# Boolean parameter stringifies to "True"/"False", so compare case-insensitively.)
TRANS_FLAG="--include-transitive"
case "$(printf '%s' "$TRANSITIVE" | tr '[:upper:]' '[:lower:]')" in
    false|no|0|"") TRANS_FLAG="" ;;
esac

# Helper: run a `dotnet list package` variant, capturing both JSON (if the SDK
# supports --format json, .NET 8+) and the plain-text fallback for older SDKs.
run_scan() {
    local name="$1"; shift
    local json_path="$OUT_DIR/${name}.json"
    local txt_path="$OUT_DIR/${name}.txt"

    echo "--- $name ---"
    # Try JSON first (SDK 8+). Suppress the error if --format is unsupported.
    if dotnet list "$TARGET" package "$@" $TRANS_FLAG --format json \
            >"$json_path" 2>/dev/null && [[ -s "$json_path" ]]; then
        echo "json -> $json_path"
    else
        rm -f "$json_path"
    fi
    # Always capture text for human-readable diffing and older SDKs.
    dotnet list "$TARGET" package "$@" $TRANS_FLAG >"$txt_path" 2>&1 || true
    echo "text -> $txt_path"
    echo ""
    cat "$txt_path"
    echo ""
}

run_scan "vulnerable" --vulnerable
run_scan "deprecated" --deprecated
run_scan "outdated"   --outdated

echo "=== Scan complete ==="
echo "Raw artifacts written to $OUT_DIR (vulnerable|deprecated|outdated .json/.txt)."
