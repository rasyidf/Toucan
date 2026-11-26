#!/usr/bin/env bash
# Records a verification result for the current commit in docs/evidence/ and commits it. See docs/evidence/README.md.
#   tools/record-evidence.sh tests [--from-results DIR] [--notes TEXT] [--no-commit] [--allow-dirty]
#   tools/record-evidence.sh check <name> --result passed|failed [--packaged] [--notes TEXT] [--no-commit] [--allow-dirty]
# --from-results DIR records TRX files from an earlier `dotnet test --logger trx` run instead of running the tests (CI).
# Needs git, the .NET SDK and python3.
set -euo pipefail

root="$(git rev-parse --show-toplevel)"
cd "$root"

usage() { sed -n '2,6p' "$0" | sed 's/^# \{0,1\}//'; exit 2; }
[ $# -ge 1 ] || usage
mode="$1"; shift
check="tests"; result=""; notes=""; packaged=false; commit_record=true; allow_dirty=false; from_results=""
if [ "$mode" = "check" ]; then
  [ $# -ge 1 ] || usage
  check="$1"; shift
elif [ "$mode" != "tests" ]; then
  usage
fi
while [ $# -gt 0 ]; do
  case "$1" in
    --result) result="${2:-}"; shift 2 ;;
    --notes) notes="${2:-}"; shift 2 ;;
    --packaged) packaged=true; shift ;;
    --no-commit) commit_record=false; shift ;;
    --allow-dirty) allow_dirty=true; shift ;;
    --from-results) from_results="${2:-}"; shift 2 ;;
    *) usage ;;
  esac
done
if ! [[ "$check" =~ ^[a-z0-9][a-z0-9-]*$ ]]; then echo "Check names are lowercase words joined by '-'." >&2; exit 2; fi
if [ "$mode" = "check" ] && [ "$result" != "passed" ] && [ "$result" != "failed" ]; then echo "--result must be passed or failed." >&2; exit 2; fi

dirty=false
if [ -n "$(git status --porcelain -- . ':(exclude)docs/evidence')" ]; then
  dirty=true
  if [ "$allow_dirty" != true ]; then echo "The working tree has uncommitted changes; commit them first or pass --allow-dirty." >&2; exit 1; fi
fi
commit="$(git rev-parse HEAD)"
version="$(python3 -c "import xml.etree.ElementTree as E; print(E.parse('Directory.Build.props').findtext('.//Version'))")"

case "$(uname -s)" in
  Darwin) family=macOS; os_version="$(sw_vers -productVersion)" ;;
  Linux) family=Linux; os_version="$( (. /etc/os-release 2>/dev/null && echo "${PRETTY_NAME:-Linux}") || uname -r)" ;;
  *) echo "Use tools/record-evidence.ps1 on Windows." >&2; exit 2 ;;
esac
arch="$(uname -m)"
environment=native
if [ "${GITHUB_ACTIONS:-}" = "true" ]; then environment=ci
elif [ "$family" = Linux ] && grep -qi microsoft /proc/version 2>/dev/null; then environment=wsl
elif [ "$family" = Linux ] && command -v systemd-detect-virt >/dev/null && systemd-detect-virt -q -v 2>/dev/null; then environment=vm
fi
ci_run=""
if [ "$environment" = ci ]; then ci_run="${GITHUB_SERVER_URL:-}/${GITHUB_REPOSITORY:-}/actions/runs/${GITHUB_RUN_ID:-}"; fi

counts_file=""; command_text=""
if [ "$mode" = "tests" ]; then
  command_text="dotnet test Toucan.slnx --configuration Release --logger trx"
  scratch="$(mktemp -d)"
  trap 'rm -rf "$scratch"' EXIT
  if [ -n "$from_results" ]; then
    results="$from_results"
  else
    results="$scratch/results"
    set +e
    dotnet test Toucan.slnx --configuration Release --logger trx --results-directory "$results"
    set -e
  fi
  counts_file="$scratch/counts.json"
  if python3 tools/trx_report.py "$results" --json "$counts_file" --markdown -; then result=passed; else result=failed; fi
fi

who="$(git config user.name || true)"; who="${who:-${USER:-unknown}}"
stamp="$(date -u +%Y%m%d-%H%M%S)"
os_slug="$(echo "$family" | tr '[:upper:]' '[:lower:]')"
file="docs/evidence/$version/$os_slug-$check-$stamp.json"
mkdir -p "docs/evidence/$version"

RECORD_FILE="$file" CHECK="$check" KIND="$([ "$mode" = tests ] && echo automated || echo manual)" RESULT="$result" COMMIT="$commit" DIRTY="$dirty" \
VERSION="$version" FAMILY="$family" OS_VERSION="$os_version" ARCH="$arch" ENVIRONMENT="$environment" PACKAGED="$packaged" \
COMMAND_TEXT="$command_text" COUNTS_FILE="$counts_file" NOTES="$notes" WHO="$who" CI_RUN="$ci_run" python3 - <<'PY'
import json, os
from datetime import datetime, timezone
e = os.environ
counts = None
if e["COUNTS_FILE"] and os.path.exists(e["COUNTS_FILE"]):
    with open(e["COUNTS_FILE"], encoding="utf-8") as f:
        c = json.load(f)
    counts = {k: c[k] for k in ("total", "passed", "failed", "skipped")}
record = {
    "schema": 1, "check": e["CHECK"], "kind": e["KIND"], "result": e["RESULT"], "commit": e["COMMIT"], "dirty": e["DIRTY"] == "true",
    "version": e["VERSION"], "os": {"family": e["FAMILY"], "version": e["OS_VERSION"], "arch": e["ARCH"]},
    "environment": e["ENVIRONMENT"], "packaged": e["PACKAGED"] == "true", "command": e["COMMAND_TEXT"] or None, "counts": counts,
    "notes": e["NOTES"], "recordedAt": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
    "recordedBy": e["WHO"], "ciRun": e["CI_RUN"] or None,
}
with open(e["RECORD_FILE"], "w", encoding="utf-8", newline="\n") as f:
    json.dump(record, f, indent=2)
    f.write("\n")
PY

echo "Recorded $check: $result -> $file"
if [ "$commit_record" = true ]; then
  git add -- "$file"
  git commit -q -m "chore(evidence): record $check on $family for ${commit:0:7}" -- "$file"
  echo "Committed $(git rev-parse --short HEAD)."
fi
[ "$result" = passed ]
