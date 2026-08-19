#!/usr/bin/env bash
set -euo pipefail

if [[ ${EUID} -eq 0 ]]; then
  echo "makepkg must run as a non-root user." >&2
  exit 1
fi
if [[ $# -ne 3 ]]; then
  echo "Usage: $0 <vX.Y.Z|X.Y.Z> <linux-binary> <output-directory>" >&2
  exit 2
fi

repo_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="${1#v}"
binary="$(realpath "$2")"
output_dir="$(realpath -m "$3")"
[[ ${version} =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid package version: ${version}" >&2; exit 2; }
[[ -f ${binary} && -x ${binary} ]] || { echo "Linux binary is missing or not executable: ${binary}" >&2; exit 2; }

work_dir="$(mktemp -d /tmp/ffix-arch-package.XXXXXXXX)"
trap 'rm -rf "${work_dir}"' EXIT
mkdir -p "${output_dir}"

install -m 0644 "${repo_dir}/packaging/arch/PKGBUILD" "${work_dir}/PKGBUILD"
install -m 0755 "${binary}" "${work_dir}/app-binary"
install -m 0644 "${repo_dir}/packaging/linux/ffix-save-editor.desktop" "${work_dir}/ffix-save-editor.desktop"
install -m 0644 "${repo_dir}/packaging/linux/io.github.nintendogamer15.FFIXSaveEditor.metainfo.xml" \
  "${work_dir}/io.github.nintendogamer15.FFIXSaveEditor.metainfo.xml"
install -m 0644 "${repo_dir}/assets/icon.png" "${work_dir}/icon.png"
install -m 0644 "${repo_dir}/LICENSE" "${work_dir}/LICENSE"
install -m 0644 "${repo_dir}/NOTICES.md" "${work_dir}/NOTICES.md"

(
  cd "${work_dir}"
  PKGVER="${version}" PKGDEST="${output_dir}" makepkg --cleanbuild --clean --noconfirm --nodeps
)

package="${output_dir}/ffix-save-editor-${version}-1-x86_64.pkg.tar.zst"
[[ -f ${package} ]] || { echo "Expected Arch package was not produced: ${package}" >&2; exit 1; }
printf '%s\n' "${package}"
