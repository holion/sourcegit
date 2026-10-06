#!/usr/bin/env bash
#
# Releases develop by pushing it to master, which starts the Release workflow.
# The workflow bumps VERSION, tags the release and builds it.
#
#   ./release.sh
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
git fetch --quiet --tags origin master

[ -n "$(git rev-list origin/master..HEAD)" ] || fail "Nothing to release: master already has everything on develop."

# The same rule as the Release workflow, to show what the next version will be.
version=$(cat VERSION)
[[ "$version" =~ ^[0-9]{4}\.[0-9]{1,2}$ ]] && version="$version.1"
while git rev-parse -q --verify "refs/tags/v$version" > /dev/null; do
  version=$(echo "$version" | awk -F. -v OFS=. '{ $NF += 1; print }')
done

git log --oneline origin/master..HEAD
read -r -p "Release these commits as v$version? [y/N] " answer
[[ "$answer" =~ ^[yY]$ ]] || fail "Aborted."

# Pick up the VERSION commits the Release workflow made on master, so master fast-forwards.
if ! git merge-base --is-ancestor origin/master HEAD; then
  git merge --quiet --no-edit origin/master
fi

git push --quiet origin develop develop:master

echo "Pushed to master. Follow the release of v$version at:"
echo "https://github.com/holion/sourcegit/actions/workflows/release.yml"
