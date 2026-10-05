#!/usr/bin/env bash

set -euo pipefail

if [ -z "${GH_TOKEN:-}" ]; then
  echo "::warning::HOMEBREW_TAP_TOKEN is not set, skipping Homebrew cask update"
  exit 0
fi

ZIP_ARM64="packages/sourcegit_$VERSION.osx-arm64.zip"
ZIP_X64="packages/sourcegit_$VERSION.osx-x64.zip"
if [ ! -f "$ZIP_ARM64" ]; then
  echo "::warning::No Apple Silicon package in this release, skipping Homebrew cask update"
  exit 0
fi

git clone "https://x-access-token:$GH_TOKEN@github.com/holion/homebrew-tap.git" tap
mkdir -p tap/Casks
CASK=tap/Casks/sourcegit-holion.rb
sed -e "s/SOURCE_GIT_VERSION/$VERSION/g" \
    -e "s/SOURCE_GIT_SHA256_ARM64/$(sha256sum "$ZIP_ARM64" | cut -d' ' -f1)/g" \
    build/resources/homebrew/sourcegit-holion.rb > "$CASK"

if [ -f "$ZIP_X64" ]; then
  sed -i -e "s/SOURCE_GIT_SHA256_X64/$(sha256sum "$ZIP_X64" | cut -d' ' -f1)/g" "$CASK"
else
  # Apple Silicon only release.
  sed -i -e 's/^  arch arm: "arm64", intel: "x64"$/  arch arm: "arm64"/' \
         -e 's/^  sha256 arm:   \("[0-9a-f]*"\),$/  sha256 \1/' \
         -e '/SOURCE_GIT_SHA256_X64/d' \
         -e 's/^  depends_on macos: ">= :ventura"$/  depends_on arch: :arm64\n  depends_on macos: ">= :ventura"/' \
         "$CASK"
fi

cd tap
git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git add Casks/sourcegit-holion.rb
git commit -m "sourcegit-holion $VERSION"
git push
