#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 6 ]]; then
  echo "Usage: $0 <arch|rpm> <owner> <arch-repository|root> <package-name> <X.Y.Z> <package-file>" >&2
  exit 2
fi
: "${GITEA_TOKEN:?GITEA_TOKEN is required}"
: "${GITEA_SERVER_URL:?GITEA_SERVER_URL is required}"
: "${REPOSITORY_NAME:?REPOSITORY_NAME is required}"

package_type="$1"
owner="$2"
registry="$3"
package_name="$4"
version="$5"
package_file="$(realpath "$6")"
[[ ${package_type} == arch || ${package_type} == rpm ]] || { echo "Unsupported package type: ${package_type}" >&2; exit 2; }
[[ ${owner} =~ ^[A-Za-z0-9._-]+$ && ${package_name} =~ ^[A-Za-z0-9._+-]+$ ]] || { echo "Unsafe package identity." >&2; exit 2; }
[[ ${version} =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid package version: ${version}" >&2; exit 2; }
[[ -f ${package_file} ]] || { echo "Package file does not exist: ${package_file}" >&2; exit 2; }

server="${GITEA_SERVER_URL%/}"
api="${server}/api/v1"
auth="Authorization: token ${GITEA_TOKEN}"
filename="$(basename "${package_file}")"
local_sha="$(sha256sum "${package_file}" | cut -d ' ' -f 1)"
registry_version="${version}-1"
work_dir="$(mktemp -d /tmp/gitea-package-publish.XXXXXXXX)"
trap 'rm -rf "${work_dir}"' EXIT

check_existing() {
  local body status existing_sha
  body="${work_dir}/files.json"
  status="$(curl --silent --show-error --output "${body}" --write-out '%{http_code}' --header "${auth}" \
    "${api}/packages/${owner}/${package_type}/${package_name}/${registry_version}/files")"
  if [[ ${status} == 404 ]]; then
    return 1
  fi
  [[ ${status} == 200 ]] || { echo "Gitea package lookup failed with HTTP ${status}:" >&2; cat "${body}" >&2; exit 1; }
  existing_sha="$(jq -r --arg name "${filename}" '[.[] | select(.name == $name)][0].sha256 // empty' "${body}")"
  if [[ -z ${existing_sha} ]]; then
    echo "Package ${package_type}/${package_name} ${registry_version} exists without ${filename}; refusing to modify it." >&2
    exit 1
  fi
  if [[ ${existing_sha} != "${local_sha}" ]]; then
    echo "Package ${filename} already exists with a different SHA-256; refusing to replace it." >&2
    echo "existing=${existing_sha} built=${local_sha}" >&2
    exit 1
  fi
  echo "Package ${filename} already exists with matching SHA-256; skipping upload."
  return 0
}

if ! check_existing; then
  if [[ ${package_type} == arch ]]; then
    [[ ${registry} =~ ^[A-Za-z0-9._-]+$ ]] || { echo "Unsafe Arch repository name: ${registry}" >&2; exit 2; }
    upload_url="${server}/api/packages/${owner}/arch/${registry}"
  else
    [[ ${registry} == root ]] || { echo "RPM packages must use the root registry." >&2; exit 2; }
    upload_url="${server}/api/packages/${owner}/rpm/upload"
  fi
  response="${work_dir}/upload-response.txt"
  status="$(curl --silent --show-error --output "${response}" --write-out '%{http_code}' \
    --request PUT --header "${auth}" --upload-file "${package_file}" "${upload_url}")"
  if [[ ${status} != 201 && ${status} != 409 ]]; then
    echo "Gitea ${package_type} upload failed with HTTP ${status}:" >&2
    cat "${response}" >&2
    exit 1
  fi
  verified=false
  for _ in 1 2 3 4 5; do
    if check_existing; then verified=true; break; fi
    sleep 2
  done
  [[ ${verified} == true ]] || { echo "Uploaded package was not visible through the Gitea package API." >&2; exit 1; }
fi

details="${work_dir}/package.json"
status="$(curl --silent --show-error --output "${details}" --write-out '%{http_code}' --header "${auth}" \
  "${api}/packages/${owner}/${package_type}/${package_name}/${registry_version}")"
[[ ${status} == 200 ]] || { echo "Gitea package detail lookup failed with HTTP ${status}:" >&2; cat "${details}" >&2; exit 1; }
linked_repository="$(jq -r '.repository.full_name // empty' "${details}")"
if [[ -z ${linked_repository} ]]; then
  repo_name="${REPOSITORY_NAME#*/}"
  response="${work_dir}/link-response.txt"
  status="$(curl --silent --show-error --output "${response}" --write-out '%{http_code}' \
    --request POST --header "${auth}" \
    "${api}/packages/${owner}/${package_type}/${package_name}/-/link/${repo_name}")"
  [[ ${status} == 201 ]] || { echo "Gitea package-to-repository link failed with HTTP ${status}:" >&2; cat "${response}" >&2; exit 1; }
  echo "Linked ${package_type}/${package_name} to ${REPOSITORY_NAME}."
elif [[ ${linked_repository} != "${REPOSITORY_NAME}" ]]; then
  echo "Package is linked to ${linked_repository}, not ${REPOSITORY_NAME}; refusing to relink it." >&2
  exit 1
fi
