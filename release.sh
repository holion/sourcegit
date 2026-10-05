#!/usr/bin/env bash
#
# Bumps VERSION, commits it and pushes a release tag, which starts the Release workflow.
#
#   ./release.sh            2026.21.2 -> 2026.21.3
#   ./release.sh 2026.22.1  release a specific version
#
# The platforms that get built come from the BUILD_RUNTIMES repository variable (default: osx-arm64).

set -euo pipefail
cd "$(dirname "$0")"

fail() {
  echo "$1" >&2
  exit 1
}

branch=$(git rev-parse --abbrev-ref HEAD)
[ "$branch" = "develop" ] || fail "Releases are made from develop, but you are on $branch."
[ -z "$(git status --porcelain)" ] || fail "Commit or stash your changes first."

git pull --ff-only --quiet origin develop

current=$(cat VERSION)
if [ $# -ge 1 ]; then
  version=${1#v}
else
  version=$(echo "$current" | awk -F. -v OFS=. '{ if (NF < 3) $3 = 1; else $NF += 1; print }')
fi

[[ "$version" =~ ^[0-9]{4}\.[0-9]{1,2}(\.[0-9]+)?$ ]] || fail "Invalid version: $version (expected e.g. 2026.21.3)."
git rev-parse -q --verify "refs/tags/v$version" > /dev/null && fail "Tag v$version already exists."
[ -z "$(git ls-remote --tags origin "refs/tags/v$version")" ] || fail "Tag v$version already exists on origin."

read -r -p "Release v$version (current: $current)? [y/N] " answer
[[ "$answer" =~ ^[yY]$ ]] || fail "Aborted."

if [ "$version" != "$current" ]; then
  printf '%s' "$version" > VERSION
  git commit --quiet -m "version: Release $version" VERSION
fi

git tag -a "v$version" -m "v$version"
git push --quiet origin develop "v$version"

echo "Released v$version. Follow the build at:"
echo "https://github.com/holion/sourcegit/actions/workflows/release.yml"
