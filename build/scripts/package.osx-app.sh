#!/usr/bin/env bash

set -e
set -o
set -u
set pipefail

cd build

mkdir -p SourceGit.app/Contents/Resources
mv SourceGit SourceGit.app/Contents/MacOS
cp resources/app/App.icns SourceGit.app/Contents/Resources/App.icns
sed "s/SOURCE_GIT_VERSION/$VERSION/g" resources/app/App.plist > SourceGit.app/Contents/Info.plist
rm -rf SourceGit.app/Contents/MacOS/SourceGit.dsym
rm -f SourceGit.app/Contents/MacOS/*.pdb

case "$RUNTIME" in
  osx-arm64) ARCH=arm64;  DMG=SourceGit-mac-apple-silicon.dmg ;;
  osx-x64)   ARCH=x86_64; DMG=SourceGit-mac-intel.dmg ;;
  *) echo "Unsupported RUNTIME: $RUNTIME" >&2; exit 1 ;;
esac

clang -arch "$ARCH" ../tools/setsid-macos/setsid.c -o SourceGit.app/Contents/MacOS/setsid -mmacosx-version-min=13.0

# Sign with the Developer ID certificate when it is available (release builds only).
SIGN=0
if [ -n "${MACOS_CERTIFICATE:-}" ]; then
  SIGN=1
  KEYCHAIN="$RUNNER_TEMP/signing.keychain-db"
  KEYCHAIN_PASSWORD=$(uuidgen)

  security create-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
  security set-keychain-settings -lut 21600 "$KEYCHAIN"
  security unlock-keychain -p "$KEYCHAIN_PASSWORD" "$KEYCHAIN"
  echo "$MACOS_CERTIFICATE" | base64 --decode > "$RUNNER_TEMP/certificate.p12"
  security import "$RUNNER_TEMP/certificate.p12" -P "$MACOS_CERTIFICATE_PASSWORD" -A -t cert -f pkcs12 -k "$KEYCHAIN"
  security set-key-partition-list -S apple-tool:,apple: -k "$KEYCHAIN_PASSWORD" "$KEYCHAIN" > /dev/null
  security list-keychains -d user -s "$KEYCHAIN" login.keychain
  IDENTITY=$(security find-identity -v -p codesigning "$KEYCHAIN" | grep "Developer ID Application" | head -n 1 | awk '{print $2}')

  # Nested binaries first, then the bundle itself.
  find SourceGit.app/Contents/MacOS -type f ! -name SourceGit | while read -r f; do
    if file -b "$f" | grep -q Mach-O; then
      codesign --force --timestamp --options runtime --sign "$IDENTITY" "$f"
    fi
  done
  codesign --force --timestamp --options runtime --sign "$IDENTITY" SourceGit.app
  codesign --verify --deep --strict --verbose=2 SourceGit.app
fi

mkdir dmg
cp -R SourceGit.app dmg/
ln -s /Applications dmg/Applications
hdiutil create -volname SourceGit -srcfolder dmg -ov -format UDZO "$DMG"
rm -rf dmg

if [ "$SIGN" = "1" ]; then
  codesign --force --timestamp --sign "$IDENTITY" "$DMG"

  echo "$APPLE_API_KEY" | base64 --decode > "$RUNNER_TEMP/api_key.p8"
  RESULT=$(xcrun notarytool submit "$DMG" \
    --key "$RUNNER_TEMP/api_key.p8" \
    --key-id "$APPLE_API_KEY_ID" \
    --issuer "$APPLE_API_ISSUER_ID" \
    --wait --output-format json)
  echo "$RESULT"

  if ! echo "$RESULT" | grep -q '"status":"Accepted"'; then
    SUBMISSION_ID=$(echo "$RESULT" | sed -n 's/.*"id":"\([^"]*\)".*/\1/p')
    xcrun notarytool log "$SUBMISSION_ID" --key "$RUNNER_TEMP/api_key.p8" --key-id "$APPLE_API_KEY_ID" --issuer "$APPLE_API_ISSUER_ID" || true
    exit 1
  fi

  # The notarization of the DMG also covers the app inside it.
  xcrun stapler staple "$DMG"
  xcrun stapler staple SourceGit.app

  security delete-keychain "$KEYCHAIN"
  rm -f "$RUNNER_TEMP/certificate.p12" "$RUNNER_TEMP/api_key.p8"
fi

ditto -c -k --keepParent SourceGit.app "sourcegit_$VERSION.$RUNTIME.zip"
