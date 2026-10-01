#!/usr/bin/env bash

set -euo pipefail

# The development branch is a Unity project. The generated UPM branch contains
# the Assets directory at its root plus repository-level documentation files.
PACKAGE_DIR="Assets"
UPM_BRANCH="upm"
REMOTE_NAME="origin"
SOURCE_REF="HEAD"

CREATE_TAG=false
PUSH_RELEASE=false
CHECK_ONLY=false
TEMP_DIR=""

print_log() {
    printf '\n==> %s\n' "$1"
}

fail() {
    printf 'error: %s\n' "$1" >&2
    exit 1
}

usage() {
    cat <<'EOF'
Usage: ./sync_upm.sh [options]

Builds a generated UPM branch from Assets without checking out or modifying the
current branch. README.md, README.zh-CN.md, LICENSE, .gitattributes, and
ScreenShots are taken from the repository root and added to the generated
package root.

Options:
  --check           Validate the source commit and package metadata only.
  --tag             Create the annotated <package-version> tag locally.
  --push            Create the version tag and atomically push branch and tag.
  --source <ref>    Source commit or branch to package (default: HEAD).
  --branch <name>   Generated branch name (default: upm).
  --remote <name>   Remote used by --push (default: origin).
  -h, --help        Show this help.

Examples:
  ./sync_upm.sh
  ./sync_upm.sh --check
  ./sync_upm.sh --tag
  ./sync_upm.sh --push
EOF
}

require_value() {
    if [[ $# -lt 2 || -z "$2" ]]; then
        fail "$1 requires a value"
    fi
}

cleanup() {
    if [[ -n "$TEMP_DIR" && -d "$TEMP_DIR" && "$TEMP_DIR" == *"/easy-time-slicing-upm."* ]]; then
        rm -rf -- "$TEMP_DIR"
    fi
}

branch_is_checked_out() {
    local expected="branch refs/heads/$UPM_BRANCH"
    local line
    while IFS= read -r line; do
        if [[ "$line" == "$expected" ]]; then
            return 0
        fi
    done < <(git worktree list --porcelain)

    return 1
}

add_root_file_to_index() {
    local source_commit="$1"
    local source_path="$2"
    local target_path="$3"
    local required="$4"
    local entry
    local mode
    local object_type
    local object_id
    local ignored_path

    entry=$(git ls-tree "$source_commit" -- "$source_path")
    if [[ -z "$entry" ]]; then
        if [[ "$required" == "true" ]]; then
            fail "$source_path must be tracked by $SOURCE_REF"
        fi

        return
    fi

    read -r mode object_type object_id ignored_path <<< "$entry"
    if [[ "$object_type" != "blob" ]]; then
        fail "$source_path is not a regular file in $SOURCE_REF"
    fi

    GIT_INDEX_FILE="$TEMP_DIR/index" git update-index \
        --add \
        --cacheinfo "$mode" "$object_id" "$target_path"
}

while [[ $# -gt 0 ]]; do
    case "$1" in
        --check)
            CHECK_ONLY=true
            shift
            ;;
        --tag)
            CREATE_TAG=true
            shift
            ;;
        --push)
            CREATE_TAG=true
            PUSH_RELEASE=true
            shift
            ;;
        --source)
            require_value "$1" "${2:-}"
            SOURCE_REF="$2"
            shift 2
            ;;
        --branch)
            require_value "$1" "${2:-}"
            UPM_BRANCH="$2"
            shift 2
            ;;
        --remote)
            require_value "$1" "${2:-}"
            REMOTE_NAME="$2"
            shift 2
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        *)
            usage >&2
            fail "unknown option: $1"
            ;;
    esac
done

trap cleanup EXIT

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)
if ! REPOSITORY_ROOT=$(git -C "$SCRIPT_DIR" rev-parse --show-toplevel 2>/dev/null); then
    fail "sync_upm.sh must be located inside a Git repository"
fi

REPOSITORY_ROOT=$(cd "$REPOSITORY_ROOT" && pwd -P)

cd "$REPOSITORY_ROOT"

if [[ "$SCRIPT_DIR" != "$REPOSITORY_ROOT" ]]; then
    fail "sync_upm.sh must remain in the repository root"
fi

if ! git check-ref-format --branch "$UPM_BRANCH" >/dev/null 2>&1; then
    fail "invalid UPM branch name: $UPM_BRANCH"
fi

if ! SOURCE_COMMIT=$(git rev-parse --verify "${SOURCE_REF}^{commit}" 2>/dev/null); then
    fail "$SOURCE_REF does not resolve to a commit; create the initial commit first"
fi

if [[ -n "$(git status --porcelain --untracked-files=normal)" ]]; then
    fail "the working tree is not clean; commit or stash all changes before syncing"
fi

if [[ "$PUSH_RELEASE" == "true" ]] && ! git remote get-url "$REMOTE_NAME" >/dev/null 2>&1; then
    fail "remote does not exist: $REMOTE_NAME"
fi

if [[ "$PUSH_RELEASE" == "true" ]]; then
    print_log "Fetching release refs"
    git fetch --prune --tags "$REMOTE_NAME"
fi

PACKAGE_MANIFEST_PATH="$PACKAGE_DIR/package.json"
if ! git cat-file -e "$SOURCE_COMMIT:$PACKAGE_MANIFEST_PATH" 2>/dev/null; then
    fail "$PACKAGE_MANIFEST_PATH is missing from $SOURCE_REF"
fi

PACKAGE_MANIFEST=$(git show "$SOURCE_COMMIT:$PACKAGE_MANIFEST_PATH")
PACKAGE_VERSION=$(printf '%s\n' "$PACKAGE_MANIFEST" \
    | sed -n 's/^[[:space:]]*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
    | sed -n '1p')

if [[ -z "$PACKAGE_VERSION" ]]; then
    fail "could not read version from $PACKAGE_MANIFEST_PATH"
fi

if [[ ! "$PACKAGE_VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$ ]]; then
    fail "package version is not valid SemVer: $PACKAGE_VERSION"
fi

for required_file in README.md README.zh-CN.md LICENSE; do
    if ! git cat-file -e "$SOURCE_COMMIT:$required_file" 2>/dev/null; then
        fail "$required_file must be tracked by $SOURCE_REF"
    fi
done

if ! SCREENSHOTS_TREE=$(git rev-parse --verify "$SOURCE_COMMIT:ScreenShots" 2>/dev/null); then
    fail "ScreenShots must be a tracked directory in $SOURCE_REF"
fi

if [[ "$(git cat-file -t "$SCREENSHOTS_TREE" 2>/dev/null)" != "tree" ]]; then
    fail "ScreenShots is not a directory in $SOURCE_REF"
fi

TAG_NAME="$PACKAGE_VERSION"

print_log "UPM release plan"
printf 'Source:  %s (%s)\n' "$SOURCE_REF" "$SOURCE_COMMIT"
printf 'Package: %s\n' "$PACKAGE_DIR"
printf 'Version: %s\n' "$PACKAGE_VERSION"
printf 'Branch:  %s\n' "$UPM_BRANCH"
if [[ "$CREATE_TAG" == "true" ]]; then
    printf 'Tag:     %s\n' "$TAG_NAME"
fi

if [[ "$CHECK_ONLY" == "true" ]]; then
    print_log "Validation passed"
    exit 0
fi

if branch_is_checked_out; then
    fail "$UPM_BRANCH is checked out in a worktree; switch that worktree to another branch first"
fi

PACKAGE_TREE=$(git rev-parse "$SOURCE_COMMIT:$PACKAGE_DIR")
TEMP_DIR=$(mktemp -d "${TMPDIR:-/tmp}/easy-time-slicing-upm.XXXXXX")

GIT_INDEX_FILE="$TEMP_DIR/index" git read-tree "$PACKAGE_TREE"

# Repository-level documentation is the sole source of truth. Remove any stale
# package-local copies before overlaying the root files into the release tree.
for generated_path in README.md README.md.meta README.zh-CN.md README.zh-CN.md.meta LICENSE LICENSE.md; do
    GIT_INDEX_FILE="$TEMP_DIR/index" git update-index \
        --force-remove -- "$generated_path" 2>/dev/null || true
done

add_root_file_to_index "$SOURCE_COMMIT" README.md README.md true
add_root_file_to_index "$SOURCE_COMMIT" README.zh-CN.md README.zh-CN.md true
add_root_file_to_index "$SOURCE_COMMIT" LICENSE LICENSE true
add_root_file_to_index "$SOURCE_COMMIT" .gitattributes .gitattributes false
GIT_INDEX_FILE="$TEMP_DIR/index" git read-tree \
    --prefix=ScreenShots/ \
    "$SCREENSHOTS_TREE"

RELEASE_TREE=$(GIT_INDEX_FILE="$TEMP_DIR/index" git write-tree)
LOCAL_BRANCH_EXISTS=false
PARENT_COMMIT=""
REMOTE_BRANCH_COMMIT=""
EXISTING_TAG_COMMIT=""

if PARENT_COMMIT=$(git rev-parse --verify "refs/heads/${UPM_BRANCH}^{commit}" 2>/dev/null); then
    LOCAL_BRANCH_EXISTS=true
fi

REMOTE_BRANCH_COMMIT=$(git rev-parse \
    --verify \
    "refs/remotes/${REMOTE_NAME}/${UPM_BRANCH}^{commit}" \
    2>/dev/null) || REMOTE_BRANCH_COMMIT=""

if [[ -z "$PARENT_COMMIT" ]]; then
    PARENT_COMMIT="$REMOTE_BRANCH_COMMIT"
fi

if [[ "$PUSH_RELEASE" == "true" \
    && "$LOCAL_BRANCH_EXISTS" == "true" \
    && -n "$REMOTE_BRANCH_COMMIT" ]] \
    && ! git merge-base --is-ancestor "$REMOTE_BRANCH_COMMIT" "$PARENT_COMMIT"; then
    fail "local $UPM_BRANCH has diverged from $REMOTE_NAME/$UPM_BRANCH; reconcile it before releasing"
fi

# Release tags are immutable. Check for conflicts before updating the generated
# branch so a reused version can never leave a partially updated local state.
if [[ "$CREATE_TAG" == "true" ]] \
    && EXISTING_TAG_COMMIT=$(git rev-parse --verify "refs/tags/${TAG_NAME}^{commit}" 2>/dev/null); then
    if [[ "$(git rev-parse "${EXISTING_TAG_COMMIT}^{tree}")" != "$RELEASE_TREE" ]]; then
        fail "tag $TAG_NAME already contains different package files; bump package.json version"
    fi

    if [[ -z "$PARENT_COMMIT" ]]; then
        PARENT_COMMIT="$EXISTING_TAG_COMMIT"
    elif [[ "$PARENT_COMMIT" != "$EXISTING_TAG_COMMIT" ]]; then
        fail "tag $TAG_NAME is not the current $UPM_BRANCH release; bump package.json version"
    fi
fi

if [[ -n "$PARENT_COMMIT" ]] && [[ "$(git rev-parse "${PARENT_COMMIT}^{tree}")" == "$RELEASE_TREE" ]]; then
    RELEASE_COMMIT="$PARENT_COMMIT"
    if [[ "$LOCAL_BRANCH_EXISTS" != "true" ]]; then
        git update-ref "refs/heads/$UPM_BRANCH" "$RELEASE_COMMIT"
    fi
    print_log "$UPM_BRANCH is already up to date"
else
    COMMIT_MESSAGE=$(printf \
        'chore(release): sync UPM package %s\n\nSource commit: %s\n' \
        "$TAG_NAME" \
        "$SOURCE_COMMIT")

    if [[ -n "$PARENT_COMMIT" ]]; then
        RELEASE_COMMIT=$(printf '%s' "$COMMIT_MESSAGE" \
            | git commit-tree "$RELEASE_TREE" -p "$PARENT_COMMIT")
        if [[ "$LOCAL_BRANCH_EXISTS" == "true" ]]; then
            git update-ref "refs/heads/$UPM_BRANCH" "$RELEASE_COMMIT" "$PARENT_COMMIT"
        else
            git update-ref "refs/heads/$UPM_BRANCH" "$RELEASE_COMMIT"
        fi
    else
        RELEASE_COMMIT=$(printf '%s' "$COMMIT_MESSAGE" \
            | git commit-tree "$RELEASE_TREE")
        git update-ref "refs/heads/$UPM_BRANCH" "$RELEASE_COMMIT"
    fi

    print_log "Updated $UPM_BRANCH"
    printf 'Commit: %s\n' "$RELEASE_COMMIT"
fi

if [[ "$CREATE_TAG" == "true" ]]; then
    if [[ -n "$EXISTING_TAG_COMMIT" ]]; then
        print_log "Tag $TAG_NAME already points to the release commit"
    else
        git tag -a "$TAG_NAME" "$RELEASE_COMMIT" -m "Release $TAG_NAME"
        print_log "Created tag $TAG_NAME"
    fi
fi

if [[ "$PUSH_RELEASE" == "true" ]]; then
    print_log "Pushing release"
    git push --atomic "$REMOTE_NAME" \
        "refs/heads/$UPM_BRANCH:refs/heads/$UPM_BRANCH" \
        "refs/tags/$TAG_NAME:refs/tags/$TAG_NAME"
fi

print_log "UPM sync completed"
printf 'Install URL: https://github.com/aillieo/EasyTimeSlicing.git#%s\n' "$TAG_NAME"
