# Linux package maintenance

The Arch and RPM packages wrap the existing self-contained `linux-x64` GUI binary; they do not rebuild or replace the save editor and do not require a system .NET runtime.

Package versions come from the release tag. Both build helpers accept either `vX.Y.Z` or `X.Y.Z`, strip the optional `v`, and set package release `1`:

```bash
scripts/build-arch-package.sh v0.3.4 /path/to/FFIXSaveEditor-v0.3.4-linux-x64 /tmp/packages
scripts/build-rpm-package.sh v0.3.4 /path/to/FFIXSaveEditor-v0.3.4-linux-x64 /tmp/packages
```

The Arch helper must run as a non-root user with `makepkg`, `desktop-file-validate`, and `appstreamcli` available. The RPM helper requires `rpmbuild`, `desktop-file-install`, and `appstreamcli`. CI builds each format in its native distribution container. The narrow `rpmlintrc` exceptions document single-file dependencies that RPM cannot discover automatically; CI separately verifies them with a clean installation, `ldd`, and an Xvfb launch.

Installed files include `/usr/bin/ffix-save-editor`, a desktop entry, a 256-pixel hicolor icon, AppStream metadata, the MIT license, and `NOTICES.md`.

On a `v*` tag, Gitea Actions publishes packages to:

- Arch repository `robert`: `https://git.11091994.xyz/api/packages/Robert/arch/robert`
- Root RPM repository: `https://git.11091994.xyz/api/packages/Robert/rpm/upload?sign=true`

Registry versions are immutable. Arch packages retain exact whole-file SHA-256 checks. RPM uploads request Gitea server signing; the helper verifies the stored RPM with the public repository key and compares signature-independent header/payload identity before accepting it.

DNF consumes the server-signed registry RPM. The RPM attached to a release remains the locally built unsigned file, so its whole-file SHA-256 intentionally differs. If a new registry RPM cannot be verified, publication stops without deleting it and the package owner may need to remove that immutable version before retrying.
