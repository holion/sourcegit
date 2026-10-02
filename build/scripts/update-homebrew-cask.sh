#!/usr/bin/env bash

set -euo pipefail

if [ -z "${GH_TOKEN:-}" ]; then
  echo "::warning::HOMEBREW_TAP_TOKEN is not set, skipping Homebrew cask update"
  exit 0
fi

SHA256_ARM64=$(sha256sum "packages/sourcegit_$VERSION.osx-arm64.zip" | cut -d' ' -f1)
SHA256_X64=$(sha256sum "packages/sourcegit_$VERSION.osx-x64.zip" | cut -d' ' -f1)

git clone "https://x-access-token:$GH_TOKEN@github.com/holion/homebrew-tap.git" tap
mkdir -p tap/Casks
sed -e "s/SOURCE_GIT_VERSION/$VERSION/g" \
    -e "s/SOURCE_GIT_SHA256_ARM64/$SHA256_ARM64/g" \
    -e "s/SOURCE_GIT_SHA256_X64/$SHA256_X64/g" \
    build/resources/homebrew/sourcegit-holion.rb > tap/Casks/sourcegit-holion.rb

cd tap
git config user.name "github-actions[bot]"
git config user.email "41898282+github-actions[bot]@users.noreply.github.com"
git add Casks/sourcegit-holion.rb
git commit -m "sourcegit-holion $VERSION"
git push
