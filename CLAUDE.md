# Holion fork of SourceGit

Fork-specific setup (install, release process, secrets) is documented in the "Holion fork" section of README.md. Read it before changing anything around versions, releases or updates.

- Every push to `master` is a release. Release with `./release.sh`, which pushes `develop` to `master`; never tag or bump `VERSION` for a release by hand. `.github/workflows/release.yml` bumps `VERSION` on `master`, tags `v*` and builds.
- When asked to release, do it without asking again: from a clean `develop`, run `echo y | ./release.sh` (it merges `origin/master` into `develop` when the workflow could not push its `VERSION` commit there, and pushes `develop` to `master`), then follow the run with `gh run watch` and report the version and assets.
- Stage only the files you changed, never `git add -A`; unrelated uncommitted work may be in the tree.
- `VERSION` carries a fork patch number on top of upstream (`2026.21.3`). Upstream merges conflict on it; keep the fork's value and set it to `<upstream>.1`, which the workflow then releases as is because it has no tag yet.
- The update check and links point at `holion/sourcegit`, not `sourcegit-scm`. Keep it that way when merging upstream changes to `src/App.axaml.cs`, `src/Views/About.axaml.cs` and `src/Views/SelfUpdate.axaml.cs`.
- macOS auto-update lives in `src/Models/AutoUpdate.cs`: it stages the release zip in the cache dir and swaps the app bundle when the app exits.
- Built runtimes come from the `runtimes` workflow input, then the `BUILD_RUNTIMES` repository variable, then `osx-arm64`.
- `build/resources/` is gitignored; new files there need `git add -f`.
- The fork is English only: `src/Resources/Locales/en_US.axaml` is merged directly in `src/App.axaml`, and there is no language setting. When merging upstream, `git rm` the other locale files (modify/delete conflicts), drop their entries in `src/App.axaml`, `src/Models/Locales.cs`, `TRANSLATION.md` and `.github/workflows/localization-check.yml`, and keep only the `en_US` text of new keys.
