#!/usr/bin/env bash
# Resolve, validate, and apply the next release version.
#
# Usage: prepare-release.sh <X.Y.Z|patch|minor|major>
#
# Edits the version file and the changelog, then prints the resolved version on
# stdout. Everything else goes to stderr, so a caller can capture the version
# with:  version="$(./scripts/prepare-release.sh patch)"
#
# Deliberately does no git writes and no network calls: the same script runs in
# CI and locally, and a maintainer can see exactly what a release would change
# without any risk of publishing it.

set -euo pipefail

# --- Configuration -----------------------------------------------------------
VERSION_FILE="Directory.Build.props"
CHANGELOG_FILE=""
TAG_PREFIX="v"

# Set to true to refuse a release when the changelog has no unreleased entries.
# The default lets the release proceed and fall back to generated notes.
REQUIRE_CHANGELOG_ENTRIES=false
# -----------------------------------------------------------------------------

script_directory="$(CDPATH='' cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(CDPATH='' cd -- "$script_directory/.." && pwd)"
cd "$repository_root"

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <X.Y.Z|patch|minor|major>" >&2
    exit 2
fi

for tool in date git head sed sort; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "$tool is required to prepare a release." >&2
        exit 1
    fi
done

read_version() {
    sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$VERSION_FILE" | head -1
}

write_version() {
    local new_version="$1"
    sed -i "s:<Version>[^<]*</Version>:<Version>$new_version</Version>:" "$VERSION_FILE"
}

[[ -f $VERSION_FILE ]] || { echo "Version file not found: $VERSION_FILE" >&2; exit 1; }

current_version="$(read_version)"
if [[ ! $current_version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "Could not read an X.Y.Z version from $VERSION_FILE, got: ${current_version:-<nothing>}" >&2
    exit 1
fi
IFS=. read -r current_major current_minor current_patch <<<"$current_version"

case "$1" in
    major) version="$((current_major + 1)).0.0" ;;
    minor) version="$current_major.$((current_minor + 1)).0" ;;
    patch) version="$current_major.$current_minor.$((current_patch + 1))" ;;
    *)
        version="${1#"$TAG_PREFIX"}"
        if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
            echo "Release version must be X.Y.Z, patch, minor, or major, got: $1" >&2
            exit 2
        fi
        ;;
esac

tag="$TAG_PREFIX$version"

# Once a tag is published, people and package registries have already fetched it,
# so a release may only ever move forward onto a version nobody has seen.
if [[ $version == "$current_version" ]]; then
    echo "Release version $version is already the current version." >&2
    exit 1
fi
if [[ "$(printf '%s\n%s\n' "$current_version" "$version" | sort -V | tail -1)" != "$version" ]]; then
    echo "Release version $version is lower than the current version $current_version." >&2
    exit 1
fi
if git rev-parse -q --verify "refs/tags/$tag" >/dev/null 2>&1; then
    echo "Tag $tag already exists; choose a new unused version." >&2
    exit 1
fi

stamp_changelog() {
    local -a lines
    local unreleased_index=-1
    local next_section_index
    local index
    local has_entries=false

    mapfile -t lines < "$CHANGELOG_FILE"
    next_section_index=${#lines[@]}
    for index in "${!lines[@]}"; do
        if [[ $unreleased_index -lt 0 ]]; then
            if [[ ${lines[index]} == "## Unreleased" || ${lines[index]} == "## [Unreleased]" ]]; then
                unreleased_index=$index
            fi
            continue
        fi
        if [[ ${lines[index]} == "## "* ]]; then
            next_section_index=$index
            break
        fi
    done

    if [[ $unreleased_index -lt 0 ]]; then
        echo "No '## Unreleased' heading in $CHANGELOG_FILE; release notes will be generated." >&2
        return 0
    fi

    for ((index = unreleased_index + 1; index < next_section_index; index++)); do
        if [[ -n ${lines[index]// /} ]]; then
            has_entries=true
            break
        fi
    done

    if [[ $has_entries != true ]]; then
        if [[ $REQUIRE_CHANGELOG_ENTRIES == true ]]; then
            echo "The '## Unreleased' section of $CHANGELOG_FILE is empty." >&2
            echo "Add the entries this release publishes, then run the release again." >&2
            exit 1
        fi
        echo "No unreleased entries in $CHANGELOG_FILE; release notes will be generated." >&2
        return 0
    fi

    {
        for ((index = 0; index <= unreleased_index; index++)); do
            printf '%s\n' "${lines[index]}"
        done
        printf '\n## %s - %s\n' "$version" "$(date -u +%F)"
        for ((index = unreleased_index + 1; index < ${#lines[@]}; index++)); do
            printf '%s\n' "${lines[index]}"
        done
    } > "$CHANGELOG_FILE.release"
    mv "$CHANGELOG_FILE.release" "$CHANGELOG_FILE"
    echo "Stamped $CHANGELOG_FILE with $version." >&2
}

if [[ -n $CHANGELOG_FILE && -f $CHANGELOG_FILE ]]; then
    stamp_changelog
fi

write_version "$version"
if [[ "$(read_version)" != "$version" ]]; then
    echo "Failed to set the version to $version in $VERSION_FILE." >&2
    exit 1
fi

echo "Prepared release $tag (was $TAG_PREFIX$current_version)." >&2
printf '%s\n' "$version"
