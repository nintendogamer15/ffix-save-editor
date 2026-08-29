#!/usr/bin/env bash
set -euo pipefail

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
path="external/save-editor-gui-framework"
expected="6ee70c4f02cdbd9790c9a64738cac97bba8e734e"
cd "${repo_dir}"

gitlink="$(git ls-files --stage "${path}" | awk '{print $2}')"
[[ ${gitlink} == "${expected}" ]] || {
  echo "Framework gitlink mismatch: expected ${expected}, got ${gitlink:-missing}." >&2
  exit 1
}
[[ -d "${path}/.git" || -f "${path}/.git" ]] || {
  echo "Framework submodule is not initialized: ${path}." >&2
  exit 1
}
actual="$(git -C "${path}" rev-parse HEAD)"
[[ ${actual} == "${expected}" ]] || {
  echo "Framework checkout mismatch: expected ${expected}, got ${actual}." >&2
  exit 1
}
[[ -z $(git -C "${path}" status --short) ]] || {
  echo "Framework submodule worktree is dirty." >&2
  exit 1
}
[[ $(git submodule status --recursive | cut -c1) == " " ]] || {
  echo "A recursive submodule is missing, modified, or at the wrong commit." >&2
  git submodule status --recursive >&2
  exit 1
}
