#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
release_label="${1:-v0.3.4}"
output_dir="${2:-${repo_dir}/artifacts}"
dotnet_cmd="${DOTNET_COMMAND:-dotnet}"
safe_label="${release_label//[^A-Za-z0-9._-]/-}"
stage_dir="$(mktemp -d /tmp/ffix-release.XXXXXXXX)"
trap 'rm -rf "${stage_dir}"' EXIT

mkdir -p "${output_dir}"
cd "${repo_dir}"
"${repo_dir}/scripts/check-submodule.sh"

"${dotnet_cmd}" restore FFIX.SaveEditor.slnx --locked-mode

"${dotnet_cmd}" publish src/FFIX.SaveEditor.Gui/FFIX.SaveEditor.Gui.csproj \
  --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  --no-restore --output "${stage_dir}/windows"
"${dotnet_cmd}" publish src/FFIX.SaveEditor.Gui/FFIX.SaveEditor.Gui.csproj \
  --configuration Release --runtime linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  --no-restore --output "${stage_dir}/linux"

# Avalonia's native runtime packs currently contribute native PDBs even when
# DebugSymbols=false. They are diagnostics, not runtime dependencies; discard them using
# the same Release publish handling as the pinned reference integration.
find "${stage_dir}/windows" "${stage_dir}/linux" -type f -name '*.pdb' -delete

[[ $(find "${stage_dir}/windows" -maxdepth 1 -type f | wc -l) -eq 1 ]] || {
  echo "Windows publish produced related files instead of one executable." >&2
  exit 1
}
[[ $(find "${stage_dir}/linux" -maxdepth 1 -type f | wc -l) -eq 1 ]] || {
  echo "Linux publish produced related files instead of one executable." >&2
  exit 1
}

windows_asset="${output_dir}/FFIXSaveEditor-${safe_label}-windows-x64.exe"
linux_asset="${output_dir}/FFIXSaveEditor-${safe_label}-linux-x64"

install -m 0644 "${stage_dir}/windows/FFIXSaveEditor.exe" "${windows_asset}"
install -m 0755 "${stage_dir}/linux/FFIXSaveEditor" "${linux_asset}"

printf '%s\n' "${windows_asset}" "${linux_asset}"
