#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 6 ]]; then
  echo "Usage: $0 <arch|rpm> <owner> <arch-repository|root> <package-name> <X.Y.Z> <package-file>" >&2
  exit 2
fi
: "${PACKAGE_PUBLISH_TOKEN:?PACKAGE_PUBLISH_TOKEN is required}"
: "${GITEA_PACKAGE_SERVER_URL:?GITEA_PACKAGE_SERVER_URL is required}"
: "${REPOSITORY_NAME:?REPOSITORY_NAME is required}"

package_type="$1"
owner="$2"
registry="$3"
package_name="$4"
version="$5"
package_file="$(realpath "$6")"
[[ ${package_type} == arch || ${package_type} == rpm ]] || { echo "Unsupported package type: ${package_type}" >&2; exit 2; }
[[ ${owner} == Robert ]] || { echo "This project publishes packages only under the Robert owner." >&2; exit 2; }
[[ ${package_name} == ffix-save-editor ]] || { echo "Unexpected package name: ${package_name}" >&2; exit 2; }
[[ ${version} =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "Invalid package version: ${version}" >&2; exit 2; }
[[ -f ${package_file} ]] || { echo "Package file does not exist: ${package_file}" >&2; exit 2; }
if [[ ${package_type} == arch ]]; then
  [[ ${registry} == robert ]] || { echo "Arch packages must use the robert repository." >&2; exit 2; }
else
  [[ ${registry} == root ]] || { echo "RPM packages must use the root registry." >&2; exit 2; }
  : "${GITEA_SERVER_URL:?GITEA_SERVER_URL is required for RPM signature verification}"
  command -v rpm >/dev/null 2>&1 || { echo "rpm is required for Gitea RPM publication." >&2; exit 1; }
fi

server="${GITEA_PACKAGE_SERVER_URL%/}"
api="${server}/api/v1"
package_user="Robert"
filename="$(basename "${package_file}")"
local_sha="$(sha256sum "${package_file}" | cut -d ' ' -f 1)"
registry_version="${version}-1"
work_dir="$(mktemp -d /tmp/gitea-package-publish.XXXXXXXX)"
trap 'rm -rf "${work_dir}"' EXIT
rpm_upload_in_progress=false
rpm_arch=""
local_rpm_identity=""
rpm_payload_digest_tag=""
rpm_payload_digest_algo_tag=""
rpm_database="${work_dir}/rpmdb"

package_failure() {
  echo "$1" >&2
  if [[ ${package_type} == rpm && ${rpm_upload_in_progress} == true ]]; then
    echo "The RPM may require deletion by the package owner before this immutable version can be published again." >&2
  fi
  exit 1
}

rpm_has_query_tag() {
  rpm --querytags | grep -Fxq "$1"
}

rpm_content_identity() {
  local rpm_file="$1" query_output header_digest payload_digest payload_digest_algorithm
  local -a identity_fields
  query_output="$(rpm --dbpath "${rpm_database}" -qp --queryformat \
    "%{SHA256HEADER}\\n%{${rpm_payload_digest_tag}}\\n%{${rpm_payload_digest_algo_tag}}\\n" \
    "${rpm_file}")" || return 1
  mapfile -t identity_fields <<<"${query_output}"
  [[ ${#identity_fields[@]} -eq 3 ]] || return 1
  header_digest="${identity_fields[0],,}"
  payload_digest="${identity_fields[1],,}"
  payload_digest_algorithm="${identity_fields[2],,}"
  [[ ${header_digest} =~ ^[0-9a-f]{64}$ && ${payload_digest} =~ ^[0-9a-f]{64}$ ]] || return 1
  [[ ${payload_digest_algorithm} == 8 || ${payload_digest_algorithm} == sha256 || ${payload_digest_algorithm} == sha-256 ]] || return 1
  printf '%s:%s:sha256\n' "${header_digest}" "${payload_digest}"
}

rpm_version_at_least_1_23() {
  local candidate="$1" major minor
  [[ ${candidate} =~ ^([0-9]+)\.([0-9]+)\.[0-9]+([-+].*)?$ ]] || return 1
  major="$((10#${BASH_REMATCH[1]}))"
  minor="$((10#${BASH_REMATCH[2]}))"
  ((major > 1 || (major == 1 && minor >= 23)))
}

prepare_rpm_verification() {
  local public_server version_file deployed_version repository_key
  [[ ${GITEA_SERVER_URL} == https://* ]] || package_failure "GITEA_SERVER_URL must use HTTPS for RPM signature verification."
  public_server="${GITEA_SERVER_URL%/}"
  version_file="${work_dir}/gitea-version.json"
  curl --fail --silent --show-error --proto '=https' --output "${version_file}" \
    "${public_server}/api/v1/version" || package_failure "Could not verify the deployed Gitea version over public HTTPS."
  deployed_version="$(jq -r '.version // empty' "${version_file}")"
  rpm_version_at_least_1_23 "${deployed_version}" || package_failure "Gitea 1.23.0 or newer is required for signed RPM uploads."
  repository_key="${work_dir}/repository.key"
  curl --fail --silent --show-error --proto '=https' --output "${repository_key}" \
    "${public_server}/api/packages/${owner}/rpm/repository.key" || package_failure "Could not download the RPM repository key."
  mkdir -m 0700 "${rpm_database}"
  rpm --dbpath "${rpm_database}" --import "${repository_key}" || package_failure "Could not import the RPM repository key."
  if rpm_has_query_tag PAYLOADSHA256 && rpm_has_query_tag PAYLOADSHA256ALGO; then
    rpm_payload_digest_tag=PAYLOADSHA256
    rpm_payload_digest_algo_tag=PAYLOADSHA256ALGO
  elif rpm_has_query_tag PAYLOADDIGEST && rpm_has_query_tag PAYLOADDIGESTALGO; then
    rpm_payload_digest_tag=PAYLOADDIGEST
    rpm_payload_digest_algo_tag=PAYLOADDIGESTALGO
  else
    package_failure "rpm cannot query a supported payload SHA-256 identity."
  fi
  rpm_has_query_tag SHA256HEADER && rpm_has_query_tag SIGPGP && rpm_has_query_tag RSAHEADER || \
    package_failure "rpm cannot query the required header/signature tags."
  rpm --checksig --nosignature "${package_file}" >/dev/null || package_failure "The locally built RPM failed digest verification."
  local_rpm_identity="$(rpm_content_identity "${package_file}")" || package_failure "Could not derive the local RPM identity."
  rpm_arch="$(rpm --dbpath "${rpm_database}" -qp --queryformat '%{ARCH}' "${package_file}")" || package_failure "Could not read RPM architecture."
  [[ ${rpm_arch} =~ ^[A-Za-z0-9_]+$ ]] || package_failure "Invalid RPM architecture: ${rpm_arch}"
}

verify_stored_rpm() {
  local api_sha="$1" public_server stored_rpm status downloaded_sha signature_tags stored_identity
  [[ ${api_sha} =~ ^[0-9a-fA-F]{64}$ ]] || package_failure "Gitea returned an invalid RPM SHA-256."
  api_sha="${api_sha,,}"
  public_server="${GITEA_SERVER_URL%/}"
  stored_rpm="${work_dir}/stored-${filename}"
  status="$(curl --silent --show-error --proto '=https' --output "${stored_rpm}" --write-out '%{http_code}' \
    "${public_server}/api/packages/${owner}/rpm/package/${package_name}/${registry_version}/${rpm_arch}/${filename}")" || \
    package_failure "Could not download the stored RPM over public HTTPS."
  [[ ${status} == 200 ]] || package_failure "Stored RPM download failed with HTTP ${status}."
  downloaded_sha="$(sha256sum "${stored_rpm}" | cut -d ' ' -f 1)"
  [[ ${downloaded_sha} == "${api_sha}" ]] || package_failure "Stored RPM bytes do not match the Gitea API SHA-256."
  signature_tags="$(rpm --dbpath "${rpm_database}" -qp --queryformat \
    '%|SIGPGP?{SIGPGP}:{missing}|:%|RSAHEADER?{RSAHEADER}:{missing}|' "${stored_rpm}")" || \
    package_failure "Could not inspect stored RPM signature tags."
  [[ ${signature_tags} == SIGPGP:RSAHEADER ]] || package_failure "The stored RPM is missing Gitea signatures."
  rpm --dbpath "${rpm_database}" --checksig "${stored_rpm}" >/dev/null || package_failure "The stored RPM signature is invalid."
  stored_identity="$(rpm_content_identity "${stored_rpm}")" || package_failure "Could not derive stored RPM identity."
  [[ ${stored_identity} == "${local_rpm_identity}" ]] || package_failure "Package ${filename} exists with different RPM content."
}

if [[ ${package_type} == rpm ]]; then
  prepare_rpm_verification
fi

check_existing() {
  local body status existing_sha
  body="${work_dir}/files.json"
  status="$(curl --silent --show-error --output "${body}" --write-out '%{http_code}' \
    --user "${package_user}:${PACKAGE_PUBLISH_TOKEN}" \
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
  if [[ ${package_type} == rpm ]]; then
    verify_stored_rpm "${existing_sha}"
    echo "Package ${filename} already exists with matching content and a valid Gitea signature; skipping upload."
  else
    if [[ ${existing_sha} != "${local_sha}" ]]; then
      echo "Package ${filename} already exists with a different SHA-256; refusing to replace it." >&2
      echo "existing=${existing_sha} built=${local_sha}" >&2
      exit 1
    fi
    echo "Package ${filename} already exists with matching SHA-256; skipping upload."
  fi
  return 0
}

if ! check_existing; then
  if [[ ${package_type} == arch ]]; then
    upload_url="${server}/api/packages/${owner}/arch/${registry}"
  else
    upload_url="${server}/api/packages/${owner}/rpm/upload?sign=true"
    rpm_upload_in_progress=true
  fi
  response="${work_dir}/upload-response.txt"
  status="$(curl --silent --show-error --output "${response}" --write-out '%{http_code}' \
    --request PUT --user "${package_user}:${PACKAGE_PUBLISH_TOKEN}" \
    --upload-file "${package_file}" "${upload_url}")"
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
  [[ ${verified} == true ]] || package_failure "Uploaded package was not visible through the Gitea package API."
  rpm_upload_in_progress=false
fi

details="${work_dir}/package.json"
status="$(curl --silent --show-error --output "${details}" --write-out '%{http_code}' \
  --user "${package_user}:${PACKAGE_PUBLISH_TOKEN}" \
  "${api}/packages/${owner}/${package_type}/${package_name}/${registry_version}")"
[[ ${status} == 200 ]] || { echo "Gitea package detail lookup failed with HTTP ${status}:" >&2; cat "${details}" >&2; exit 1; }
linked_repository="$(jq -r '.repository.full_name // empty' "${details}")"
if [[ -z ${linked_repository} ]]; then
  repo_name="${REPOSITORY_NAME#*/}"
  response="${work_dir}/link-response.txt"
  status="$(curl --silent --show-error --output "${response}" --write-out '%{http_code}' \
    --request POST --user "${package_user}:${PACKAGE_PUBLISH_TOKEN}" \
    "${api}/packages/${owner}/${package_type}/${package_name}/-/link/${repo_name}")"
  if [[ ${status} == 201 ]]; then
    echo "Linked ${package_type}/${package_name} to ${REPOSITORY_NAME}."
  else
    echo "WARNING: Gitea package-to-repository link failed with HTTP ${status}; package publication remains successful:" >&2
    cat "${response}" >&2
    echo "WARNING: Link ${package_type}/${package_name} to ${REPOSITORY_NAME} manually in Gitea if needed." >&2
  fi
elif [[ ${linked_repository} != "${REPOSITORY_NAME}" ]]; then
  echo "Package is linked to ${linked_repository}, not ${REPOSITORY_NAME}; refusing to relink it." >&2
  exit 1
fi
