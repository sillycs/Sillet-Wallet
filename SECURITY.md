# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private "Report a vulnerability" button on this repository (Security tab), or write to the Raton team at [raton.fun](https://raton.fun)

Include the version, what you did, what you expected and what happened. You will get an answer within 7 days, and a fix or a plan within 30 days for confirmed issues. Reporters are credited in the release notes unless they prefer to stay anonymous

## Scope

Key generation and derivation, signing for every network, the encrypted vault, the update mechanism and anything that could make the app send funds or reveal secrets without the user's consent

## How updates are protected

- Releases are built only by GitHub Actions from a tagged commit and carry a build provenance attestation (`gh attestation verify`)
- Every Windows update is signed offline with an Ed25519 release key. The public key is compiled into the app (`UpdateKey.PublicKeyHex`) and the app refuses any update without a valid signature over the version, the SHA-256 of the file and the changelog
- Automatic updates are off by default
- Updates are never installed silently. The app shows the version and changelog and waits for the user to press Install
- Administrator permission is requested only when Sillet lives in a protected folder such as Program Files
- Android updates are installed by the user from the APK, which is signed with a stable key. Its certificate fingerprint is published with every release

See [docs/RELEASING.md](docs/RELEASING.md) for how releases are built, signed and verified, and [docs/ENDPOINTS.md](docs/ENDPOINTS.md) for every server the app contacts
