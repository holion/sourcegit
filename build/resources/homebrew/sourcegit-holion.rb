cask "sourcegit-holion" do
  arch arm: "arm64", intel: "x64"

  version "SOURCE_GIT_VERSION"
  sha256 arm:   "SOURCE_GIT_SHA256_ARM64",
         intel: "SOURCE_GIT_SHA256_X64"

  url "https://github.com/holion/sourcegit/releases/download/v#{version}/sourcegit_#{version}.osx-#{arch}.zip"
  name "SourceGit (Holion)"
  desc "Holion fork of the SourceGit Git GUI client"
  homepage "https://github.com/holion/sourcegit"

  livecheck do
    url :url
    strategy :github_latest
  end

  conflicts_with cask: "sourcegit"
  depends_on macos: ">= :ventura"

  app "SourceGit.app"

  # The app is not notarized, so strip the quarantine flag to keep Gatekeeper from blocking it.
  postflight do
    system_command "/usr/bin/xattr", args: ["-cr", "#{appdir}/SourceGit.app"]
  end
end
