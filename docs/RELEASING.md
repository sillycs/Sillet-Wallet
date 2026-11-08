# Releasing Sillet

Releases are built by GitHub Actions and nothing else. The workflow `.github/workflows/release.yml` runs when a tag like `v1.1.5` is pushed. It runs the tests, builds every file from that commit, writes `SHA256SUMS.txt`, attaches a build provenance attestation and creates the GitHub Release. Nobody uploads binaries by hand

## One time setup

1. Create the offline update key with `keygen.ps1`. It writes the private key to a file (move it to a USB drive and back it up, never commit it) and puts the public key in `UpdateKey.PublicKeyHex`. Commit that change. The workflow refuses to release while the public key is empty
2. Create the Android signing key once and keep it forever. Every release must be signed with the same key:

   ```
   keytool -genkeypair -v -keystore sillet.keystore -alias sillet -keyalg RSA -keysize 4096 -validity 10000
   keytool -list -v -keystore sillet.keystore -alias sillet
   ```

   Add the repository secrets `ANDROID_KEYSTORE_B64` (base64 of the keystore file), `ANDROID_KEYSTORE_PASSWORD` and `ANDROID_KEY_ALIAS`. Publish the SHA-256 certificate fingerprint. Without the secrets the release has no APK
3. In the GitHub repository settings, add a tag ruleset for `v*` that blocks deleting and updating tags, and only lets maintainers create them

## Each release

1. Bump the version: `UpdateService.cs`, `src/Sillet.Cli/Program.cs`, `Sillet.Cli.csproj`, `extension/package.json` (same version) and the Android `ApplicationDisplayVersion` and `ApplicationVersion`. Edit `RELEASE_NOTES.md` (plain ASCII, under 1500 characters, it is shown in the update prompt)
2. Run `release.ps1`. It restores the packages so every `packages.lock.json` is committed. Tags are never moved, deleted or force-pushed: a published version is never rebuilt, a fix is a new version.
   Run `release.ps1`. It pushes the source, pushes the tag, waits for the workflow, checks the attestation of every file, signs `version.json` with the offline key and attaches it to the same GitHub release

## Verifying a download

- `gh attestation verify Sillet.exe --repo <owner>/<repo>` proves the file was built by the workflow from a tagged commit
- Compare the SHA-256 with `SHA256SUMS.txt` on the GitHub Release
- Android: `apksigner verify --print-certs Sillet.apk` and compare with the published fingerprint
- The app itself only accepts an update whose `version.json` is signed by the release key and whose file matches the signed SHA-256

## Reproducing a build

Builds are deterministic (`Deterministic`, `PathMap` and `ContinuousIntegrationBuild` are set). Check out the tag, install the SDKs listed in the workflow, run the same commands and compare the SHA-256 of `Sillet.exe`
