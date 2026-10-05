# Holion fork of SourceGit

Fork-specific setup (install, release process, secrets) is documented in the "Holion fork" section of README.md. Read it before changing anything around versions, releases or updates.

- Release with `./release.sh`; never tag by hand. It bumps `VERSION` and pushes the `v*` tag that triggers `.github/workflows/release.yml`.
- `VERSION` carries a fork patch number on top of upstream (`2026.21.3`). Upstream merges conflict on it; keep the fork's value and bump to `<upstream>.1`.
- The update check and links point at `holion/sourcegit`, not `sourcegit-scm`. Keep it that way when merging upstream changes to `src/App.axaml.cs`, `src/Views/About.axaml.cs` and `src/Views/SelfUpdate.axaml.cs`.
- macOS auto-update lives in `src/Models/AutoUpdate.cs`: it stages the release zip in the cache dir and swaps the app bundle when the app exits.
- Built runtimes come from the `runtimes` workflow input, then the `BUILD_RUNTIMES` repository variable, then `osx-arm64`.
- `build/resources/` is gitignored; new files there need `git add -f`.
