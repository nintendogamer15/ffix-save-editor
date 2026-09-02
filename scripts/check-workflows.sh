#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${repo_dir}"

for workflow in .github/workflows/*.yml; do
  grep -q 'actions/checkout@11d5960a326750d5838078e36cf38b85af677262' "${workflow}"
  grep -q 'persist-credentials: false' "${workflow}"
  grep -q 'submodules: recursive' "${workflow}"
  grep -q -- '--locked-mode' "${workflow}"
done
grep -q 'actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9' .github/workflows/test.yml
grep -q 'actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9' .github/workflows/release.yml
grep -q 'actions/upload-artifact@ea165f8d65b6e75b540449e92b4886f43607fa02' .github/workflows/release.yml

if grep -nE '^ {4,8}(GITEA_TOKEN|PACKAGE_PUBLISH_TOKEN):' .gitea/workflows/*.yml; then
  echo "Gitea credentials must not be declared at job-wide scope." >&2
  exit 1
fi
grep -q 'dotnet restore FFIX.SaveEditor.slnx --locked-mode' .gitea/workflows/release.yml
test "$(grep -c 'git submodule update --init --recursive' .gitea/workflows/release.yml)" -eq 3
test "$(grep -c 'working-directory: source' .gitea/workflows/release.yml)" -eq 5
