#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
release_label="${1:-v0.3.0}"
output_dir="${2:-${repo_dir}/artifacts}"
dotnet_cmd="${DOTNET_COMMAND:-dotnet}"
safe_label="${release_label//[^A-Za-z0-9._-]/-}"
stage_dir="$(mktemp -d /tmp/ffix-release.XXXXXXXX)"
trap 'rm -rf "${stage_dir}"' EXIT

mkdir -p "${output_dir}"
cd "${repo_dir}"

"${dotnet_cmd}" publish src/FFIX.SaveEditor.Gui/FFIX.SaveEditor.Gui.csproj \
  --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  --output "${stage_dir}/windows"
"${dotnet_cmd}" publish src/FFIX.SaveEditor.Gui/FFIX.SaveEditor.Gui.csproj \
  --configuration Release --runtime linux-x64 --self-contained true \
  -p:PublishSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false \
  --output "${stage_dir}/linux"

windows_asset="${output_dir}/FFIXSaveEditor-${safe_label}-windows-x64.exe"
linux_asset="${output_dir}/FFIXSaveEditor-${safe_label}-linux-x64"
source_asset="${output_dir}/ffix-save-editor-${safe_label}-source.tar.gz"

install -m 0644 "${stage_dir}/windows/FFIXSaveEditor.exe" "${windows_asset}"
install -m 0755 "${stage_dir}/linux/FFIXSaveEditor" "${linux_asset}"

archive_files=()
while IFS= read -r -d '' source_file; do
  [[ -f "${source_file}" ]] && archive_files+=("${source_file}")
done < <(git ls-files -z --cached --others --exclude-standard)
tar -czf "${source_asset}" "${archive_files[@]}"

printf '%s\n' "${windows_asset}" "${linux_asset}" "${source_asset}"
