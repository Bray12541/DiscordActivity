# GitHub Releases setup

The app can update directly from a public GitHub repository without an API token.

## One-time repository setup

1. Create a GitHub repository and put the source files from this folder at its root.
2. Create `.github/workflows/release.yml`.
3. Copy the contents of `github-release-workflow.yml` into that file.
4. In Discord Activity, open **Backup & updates** and enter the repository as
   `owner/repository`.

## Publishing an update

1. Increase `<Version>` in `DiscordActivity.csproj`, such as `2.2.0`.
2. Commit and push the source change.
3. Create and push a matching tag:

   ```powershell
   git tag v2.2.0
   git push origin v2.2.0
   ```

The workflow builds the self-contained Windows executable, generates
`DiscordActivity.sha256`, and publishes both files in a GitHub Release.

The application checks the repository's latest published release. It verifies the
download using GitHub's asset digest or `DiscordActivity.sha256`, asks for
confirmation, then replaces the executable after the current process exits.

Draft and prerelease releases are not returned by GitHub's latest-release endpoint.
