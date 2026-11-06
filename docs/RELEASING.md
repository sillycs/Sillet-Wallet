# Releasing Sillet

## One time setup

1. Create the offline update key (do this on a machine that is not connected to the internet and keep the file offline, never commit it):

   ```
   dotnet run --project tools/ReleaseSigner -- keygen release-key.key
   ```

   Paste the public key it prints into `UpdateKey.PublicKeyHex` in `src/Sillet.App/Services/UpdateKey.cs`. Until a key is set the app refuses to self-update and only links to the download page

2. Create the Android signing key once and keep it forever. The same key must sign every release:

   ```
   keytool -genkeypair -v -keystore sillet.keystore -alias sillet -keyalg RSA -keysize 4096 -validity 10000
   keytool -list -v -keystore sillet.keystore -alias sillet
   ```

   Publish the SHA-256 certificate fingerprint in the README and in each release. Store the keystore as the GitHub secrets `ANDROID_KEYSTORE_B64`, `ANDROID_KEYSTORE_PASSWORD` and `ANDROID_KEY_ALIAS`

3. Run `dotnet restore` once and commit the generated `packages.lock.json` files so dependencies are pinned. Dependabot proposes updates as pull requests

## Each release

1. Update the version in `AppInfo.Version`, the Android `ApplicationDisplayVersion` and `version.json`
2. Run `.\build.ps1 -Release` (needs the GitHub CLI: `gh auth login`). It builds, pushes the source code, creates the GitHub Release with every file and updates the raton.fun download page. The `Release` workflow is a manual backup (Actions tab, Run workflow)
3. The `Release` workflow builds everything from that commit, runs the tests, writes `SHA256SUMS.txt`, attaches a build provenance attestation and publishes the GitHub Release
4. Download `Sillet.exe` from the release, then sign it offline:

   ```
   dotnet run --project tools/ReleaseSigner -- sign release-key.key Sillet.exe 1.1.5 notes.txt
   ```

   Put the printed `version`, `sha256`, `notes` and `sig` fields, plus `exe` pointing at the same binary, in `version.json` on raton.fun

## Verifying a download

- Compare the SHA-256 with `SHA256SUMS.txt` on the GitHub Release (a hash served from the same site as the file proves nothing on its own, so use the release page)
- `gh attestation verify Sillet.exe --repo <owner>/<repo>` checks that the file was built by the workflow from a tagged commit
- Android: `apksigner verify --print-certs Sillet.apk` and compare the fingerprint with the published one

## Reproducing a build

Builds are deterministic (`Deterministic`, `PathMap` and `ContinuousIntegrationBuild` are set). Check out the tag, install the SDKs listed in the workflow, run the same commands and compare the SHA-256 of `Sillet.exe`
