# Publishing `yay_see_sharp` to the AUR

This is a practical, step-by-step guide for packaging and publishing `yay_see_sharp` to the Arch
User Repository (AUR). It assumes no prior AUR packaging experience.

> **Status:** this guide documents how to package the app; it does not mean a package has actually
> been submitted yet. Treat it as a runbook for whoever does the first submission (and every
> update after it).

## 1. What a PKGBUILD is

A `PKGBUILD` is a bash script that tells `makepkg` (and, through it, `yay`/`paru`) how to build and
install a package from source. For an AUR package it's the **only** file that actually matters —
everything else (`.SRCINFO`, the git repo on `aur.archlinux.org`) is derived from or alongside it.

The fields relevant to a .NET/Avalonia app like this one:

| Field | Purpose |
| --- | --- |
| `pkgname` | The AUR package name — see naming below. |
| `pkgver` | Upstream version, e.g. `1.0.0`. Must not contain a hyphen. |
| `pkgrel` | Package-only revision (bump when you change the PKGBUILD without a new upstream version — e.g. fixing a packaging bug). Starts at `1`, resets to `1` on every `pkgver` bump. |
| `pkgdesc` | One-line description shown in `pacman -Qi` / `yay -Ss`. |
| `arch` | `('x86_64')` — .NET 10 on Arch only ships `x86_64` runtime packages today. |
| `url` | Upstream project URL (the GitHub repo). |
| `license` | `('GPL3')` — matches this repo's `LICENSE`. |
| `depends` | Runtime dependencies — see §2 and §3. |
| `makedepends` | Build-time-only dependencies (the .NET SDK, not the runtime). |
| `source` | Where `makepkg` downloads the source from — a GitHub release tag/tarball URL, parameterized by `$pkgver`. |
| `sha256sums` | Checksum of each `source` entry, so `makepkg` refuses to build against tampered/corrupted source. |
| `build()` | Shell function: compiles the app (`dotnet publish`). |
| `package()` | Shell function: copies build output, `.desktop` file, icon, and any other files into `$pkgdir`, mirroring the final filesystem layout under `/usr`. |

Two package name conventions apply here:

- **`yay-see-sharp`** (a regular AUR package, source-built from a tagged release) — the name used
  throughout this guide.
- If a `-bin` (pre-built binary, no compilation) or `-git` (builds from the latest commit, not a
  tagged release) variant is ever added, they'd be separate AUR packages: `yay-see-sharp-bin`,
  `yay-see-sharp-git`. Not covered here — start with the plain source package.

## 2. The .NET runtime dependency

`yay_see_sharp` targets .NET 10. Two ways to satisfy that at runtime:

### Option A — depend on `dotnet-runtime` (recommended)

Ship a **framework-dependent** build (`dotnet publish` without `--self-contained`) and add
`dotnet-runtime` (or the specific `dotnet-runtime-10.0` package once it exists in the Arch
repos/AUR) to `depends`. This is what `.github/workflows/release.yml`'s `dotnet publish` step
already produces — no build changes needed.

Pros: small package (~tens of MB, not the runtime's ~100+ MB bundled per app), the runtime gets
security updates independently via `pacman -Syu`, consistent with how other .NET AUR packages
(e.g. anything built on Avalonia) are typically packaged.

Cons: user needs `dotnet-runtime` installed — but that's exactly what `depends` handles
automatically; `yay -S yay-see-sharp` pulls it in.

### Option B — self-contained, bundled runtime

`dotnet publish --self-contained --runtime linux-x64` bundles the runtime into the app's own
output directory. No `dotnet-runtime` dependency needed at all.

Pros: fully self-contained, no runtime version conflicts with other .NET apps.
Cons: much larger package, the bundled runtime doesn't get pacman-managed security updates, and
AUR reviewers/`namcap` generally frown on bundling a system-level runtime unless there's a real
reason (there isn't one here).

**Use Option A.** The PKGBUILD template in §4 assumes it.

## 3. The `yay` dependency

`yay_see_sharp` is a front-end for `yay` — but per `docs/product-requirements.md` and
`docs/implementation-status.md`, the app runs fine in **Demo mode** without `yay` on `PATH` (any
non-Arch host, or Arch/CachyOS without `yay` installed), and on Arch/CachyOS without `yay` it
offers an in-app install flow (`BackendInstallPromptViewModel`) rather than refusing to start.

That means `yay` must **not** be a hard `depends` entry — a hard dependency would force every
install of `yay-see-sharp` to also pull in `yay`, defeating the point of Demo mode existing at all
(e.g. someone packaging/testing this on a non-Arch-primary system via a container, or evaluating
the app before committing to installing `yay`).

Use `optdepends` instead:

```bash
optdepends=('yay: real package management instead of Demo mode')
```

This shows up in `pacman -Qi yay-see-sharp` / `yay -Qi yay-see-sharp` as an optional dependency
with an explanation, without forcing the install. Real mode activates automatically the moment
`yay` is on `PATH` (see `IEngineDetector`/`PackageBackendFactory` in
`source/yay_see_sharp.infrastructure`) — no package-level action needed beyond installing `yay`
itself (`pacman -S yay` on CachyOS, or the AUR bootstrap on plain Arch — which the app's own
in-app installer can also do).

## 4. PKGBUILD template

```bash
# Maintainer: Your Name <you@example.com>
pkgname=yay-see-sharp
pkgver=1.0.0
pkgrel=1
pkgdesc="Avalonia UI front-end for the yay AUR helper"
arch=('x86_64')
url="https://github.com/StipecMV/yay_see_sharp"
license=('GPL3')
depends=('dotnet-runtime' 'hicolor-icon-theme')
makedepends=('dotnet-sdk')
optdepends=('yay: real package management instead of Demo mode')
provides=('yay_see_sharp')
conflicts=('yay_see_sharp')
source=("$pkgname-$pkgver.tar.gz::https://github.com/StipecMV/yay_see_sharp/archive/refs/tags/v$pkgver.tar.gz")
sha256sums=('SKIP')  # replace with the real sha256sum before submitting — see §6

build() {
  cd "yay_see_sharp-$pkgver"
  dotnet publish source/yay_see_sharp.application/yay_see_sharp.application.csproj \
    --configuration Release \
    --output "$srcdir/publish" \
    --no-self-contained \
    -p:UseAppHost=true
}

package() {
  cd "yay_see_sharp-$pkgver"

  # Application files
  install -d "$pkgdir/usr/lib/yay-see-sharp"
  cp -r "$srcdir/publish/." "$pkgdir/usr/lib/yay-see-sharp/"

  # Launcher script: /usr/bin/yay-see-sharp -> the published apphost binary. A wrapper script
  # (rather than a raw symlink to the apphost) keeps the door open for pre-launch env vars later
  # without touching the .desktop file's Exec= again.
  install -d "$pkgdir/usr/bin"
  cat > "$pkgdir/usr/bin/yay-see-sharp" <<'EOF'
#!/bin/sh
exec /usr/lib/yay-see-sharp/yay_see_sharp.application "$@"
EOF
  chmod 755 "$pkgdir/usr/bin/yay-see-sharp"

  # Desktop entry + icon (see §8/§9)
  install -Dm644 packaging/yay-see-sharp.desktop \
    "$pkgdir/usr/share/applications/yay-see-sharp.desktop"
  install -Dm644 packaging/icons/yay-see-sharp.png \
    "$pkgdir/usr/share/icons/hicolor/256x256/apps/yay-see-sharp.png"

  # License
  install -Dm644 LICENSE "$pkgdir/usr/share/licenses/$pkgname/LICENSE"
}
```

Notes on choices made above:

- `provides`/`conflicts` on `yay_see_sharp` (underscore form) exists so if anyone ever manually
  installs a same-named package built with the underscore spelling, pacman treats them as the same
  logical package rather than silently allowing both side by side.
- `hicolor-icon-theme` in `depends` ensures the icon actually shows up in app launchers (it's what
  provides the icon-cache infrastructure most desktop environments rely on).
- `-p:UseAppHost=true` (usually the default, listed explicitly for clarity) produces a native Linux
  executable (`yay_see_sharp.application`) instead of requiring `dotnet yay_see_sharp.application.dll`.
- `sha256sums=('SKIP')` is a placeholder — **never actually submit with `SKIP`** (see §6); it's
  only here so the template is copy-pasteable before a real tag exists.

## 5. Desktop integration files

### `.desktop` file

Already checked into this repo at [`packaging/yay-see-sharp.desktop`](../packaging/yay-see-sharp.desktop):

```ini
[Desktop Entry]
Type=Application
Name=Yay See Sharp
Comment=Avalonia UI front-end for the yay AUR helper
Exec=yay-see-sharp
Icon=yay-see-sharp
Terminal=false
Categories=System;PackageManager;
StartupWMClass=yay_see_sharp.application
```

- `Exec` matches the `/usr/bin/yay-see-sharp` wrapper script installed by the PKGBUILD, not the raw
  build-output binary name (`yay_see_sharp.application`).
- `StartupWMClass` matches the *actual* window class Avalonia reports at runtime, which does come
  from the build output's assembly name (`yay_see_sharp.application`) — this is what desktop
  environments use to associate a running window back to this `.desktop` entry (taskbar
  grouping, "pin to taskbar", etc.), so it intentionally does **not** match `Exec`.

### Icon

Already checked into this repo at [`packaging/icons/yay-see-sharp.png`](../packaging/icons/yay-see-sharp.png)
(256×256 PNG, extracted from the existing multi-resolution
`source/yay_see_sharp.application/Assets/app-icon.ico`, which already ships 16/24/32/48/64/128/256
px frames). Installed to the standard hicolor icon theme path:

```text
/usr/share/icons/hicolor/256x256/apps/yay-see-sharp.png
```

If a proper multi-resolution icon set is ever wanted, extract each embedded size from the same
`.ico` (they're already there) and install each under its matching
`/usr/share/icons/hicolor/<size>x<size>/apps/yay-see-sharp.png` path — one 256×256 PNG is a
reasonable minimum for AUR submission and is what the template in §4 installs.

## 6. Versioning and updating the PKGBUILD after a new release

This repo's `release.yml` workflow (`workflow_dispatch`, manual) tags a GitHub release as
`v<version>` (e.g. `v1.2.0`) and attaches a `yay_see_sharp-v<version>-linux-x64.zip` publish
artifact. The AUR package tracks the **source tag**, not that prebuilt zip (the zip is a
convenience download, not what `makepkg` should build from) — `source=` in §4 points at
`archive/refs/tags/v$pkgver.tar.gz`, GitHub's auto-generated source tarball for that tag.

On every new upstream release:

1. Bump `pkgver` to match the new tag (without the `v` prefix — pacman version strings can't start
   with a non-numeric character the way `v1.2.0` does).
2. Reset `pkgrel=1` (a new upstream version always resets the packaging revision).
3. Recompute the checksum:
   ```bash
   curl -sL "https://github.com/StipecMV/yay_see_sharp/archive/refs/tags/v<version>.tar.gz" | sha256sum
   ```
   Paste the result into `sha256sums=(...)`, replacing `SKIP`.
4. Rebuild and test locally (§7).
5. Regenerate `.SRCINFO` (required — the AUR git repo's canonical metadata, not the PKGBUILD
   itself):
   ```bash
   makepkg --printsrcinfo > .SRCINFO
   ```
6. Commit both `PKGBUILD` and `.SRCINFO`, push to the AUR git remote (see §7 for the remote setup).

If only the *packaging* needs a fix (not a new upstream version — e.g. a missing dependency was
discovered), leave `pkgver` alone and bump `pkgrel` instead (`pkgrel=2`, etc.), then repeat steps
4–6.

## 7. AUR submission steps

### 7.1 Create an AUR account

1. Register at https://aur.archlinux.org/register/.
2. Add an SSH public key under Account → My Account (needed to push to the AUR git remote — AUR
   git access is SSH-only, no HTTPS/password push).

### 7.2 Build and test the PKGBUILD locally first

Never submit a PKGBUILD that hasn't actually built successfully on a real Arch/CachyOS machine:

```bash
mkdir -p ~/aur/yay-see-sharp && cd ~/aur/yay-see-sharp
# put the PKGBUILD from §4 here, with a real pkgver + sha256sum

makepkg -si   # build, then install, prompting for sudo as needed
```

`makepkg -si` will fail loudly on a missing `makedepends`/`depends` entry, a bad checksum, or a
`build()`/`package()` script bug — fix those before going anywhere near the AUR itself.

### 7.3 Run `namcap`

`namcap` is the standard AUR linter — checks for missing dependencies, incorrect permissions,
non-standard paths, and other packaging mistakes AUR reviewers will otherwise flag manually.

```bash
sudo pacman -S --needed namcap
namcap PKGBUILD
namcap yay-see-sharp-<version>-<pkgrel>-x86_64.pkg.tar.zst   # after makepkg -s, before -i
```

Fix everything `namcap` reports before submitting. Common findings for a .NET app package:
missing `depends` for a shared library the runtime needs (rare with `dotnet-runtime` as a proper
dependency, but check), or a file installed outside the expected `/usr/lib/<pkgname>/` /
`/usr/share/` layout.

### 7.4 Push to the AUR

```bash
git clone ssh://aur@aur.archlinux.org/yay-see-sharp.git
cd yay-see-sharp
cp /path/to/PKGBUILD .
makepkg --printsrcinfo > .SRCINFO
git add PKGBUILD .SRCINFO
git commit -m "Initial import: yay-see-sharp 1.0.0-1"
git push
```

The package is live on the AUR (`https://aur.archlinux.org/packages/yay-see-sharp`) immediately
after the push — there's no separate approval/review step for a *new* package, but community
comments and flags (out-of-date, etc.) apply from then on, and a maintainer is expected to respond
to them.

### 7.5 Test the actual AUR install path

After pushing, verify the package installs the way an end user would (via `yay`/`paru`, not a
local `makepkg -si`):

```bash
yay -S yay-see-sharp
```

## 8. `install` script (post-install hooks)

A `.install` file (referenced from the PKGBUILD via `install=yay-see-sharp.install`) runs shell
snippets at `pacman` install/upgrade/remove time — used here to refresh the desktop/icon caches so
the new `.desktop` entry and icon show up immediately without the user having to log out and back
in:

```bash
# yay-see-sharp.install

post_install() {
  update-desktop-database -q /usr/share/applications &> /dev/null || true
  gtk-update-icon-cache -q -t -f /usr/share/icons/hicolor &> /dev/null || true
}

post_upgrade() {
  post_install
}

post_remove() {
  post_install
}
```

Reference it from the PKGBUILD:

```bash
install=yay-see-sharp.install
```

(and add the `.install` file itself to a `source=()` entry, or keep it alongside the PKGBUILD in
the same AUR git repo — either works; the AUR git repo is the source of truth either way.)

Both cache-refresh commands are best-effort (`|| true`) — a missing `update-desktop-database` or
`gtk-update-icon-cache` binary (e.g. a minimal window manager setup without a full desktop
environment) must never fail the package install/upgrade itself.

## 9. Checklist before submitting/updating on the AUR

- [ ] `pkgver` matches the exact upstream release tag (no `v` prefix).
- [ ] `pkgrel` is `1` for a new `pkgver`, or bumped for a packaging-only fix.
- [ ] `sha256sums` is the real checksum of the actual tarball `source=` points at — never `SKIP`.
- [ ] `depends=('dotnet-runtime' 'hicolor-icon-theme')` — not a bundled/self-contained runtime.
- [ ] `yay` is in `optdepends`, **not** `depends` — Demo mode must keep working without it.
- [ ] `makepkg -si` succeeds on a clean Arch/CachyOS machine (or container) from scratch.
- [ ] `namcap PKGBUILD` and `namcap <built package>` both come back clean.
- [ ] The app actually launches post-install (`yay-see-sharp` from a terminal, and from the
      applications menu — confirms the `.desktop` file + icon path are correct).
- [ ] `.desktop` file's `Exec=` matches the installed launcher path/name exactly.
- [ ] Icon renders in the app launcher (not a broken-image placeholder) — confirms the
      `hicolor`/icon-cache path and `post_install` hook are correct.
- [ ] `.SRCINFO` was regenerated (`makepkg --printsrcinfo > .SRCINFO`) and committed alongside the
      `PKGBUILD` — a stale `.SRCINFO` is one of the most common AUR review rejections.
- [ ] License file installed at `/usr/share/licenses/yay-see-sharp/LICENSE`.
- [ ] Package builds and installs as a normal (non-root) user via `makepkg`, only using `sudo`
      internally for the final `pacman -U` install step (never as the build user throughout).
