# Setting up a self-hosted GitHub Actions runner on CachyOS

This walks through provisioning a **CachyOS** (or plain Arch Linux) machine as a self-hosted
GitHub Actions runner for `yay_see_sharp`. The CI workflow (`.github/workflows/ci.yml`) and
release workflow (`.github/workflows/release.yml`) both target `runs-on: [self-hosted, cachyos]`,
so this runner is what actually builds, tests (including the gated real-Arch integration tests),
and publishes the project.

Assumes a clean CachyOS installation with a regular (non-root) user account and `sudo` available.
Every command below is copy-pasteable; replace the placeholders (`YOUR_GITHUB_TOKEN`,
`YOUR_GITHUB_REPO`, `YOUR_RUNNER_USER`) with your actual values before running.

---

## 1. Prerequisites

Install the packages the build and test suite need. `yay` itself is required — without it on
`PATH`, `BackendMode.Unavailable` kicks in and the Real-mode-dependent parts of the suite (and the
gated integration tests) won't exercise what they're meant to.

```bash
# Base toolchain for building AUR packages (needed to install yay itself, and by
# YayBackendInstaller's own AUR bootstrap path if it's ever exercised).
sudo pacman -Syu --needed base-devel git

# .NET 10 SDK — check the AUR for the current package name if this one has moved.
yay -S --needed dotnet-sdk-bin

# yay itself, if this is a fresh CachyOS/Arch install that doesn't already have it.
# CachyOS ships yay in its own repos:
sudo pacman -S --needed --noconfirm yay
# On plain Arch (no yay yet, so no yay to bootstrap yay with):
#   git clone https://aur.archlinux.org/yay.git /tmp/yay-bootstrap
#   cd /tmp/yay-bootstrap && makepkg -si --noconfirm

# Verify:
dotnet --version   # expect 10.x
yay --version
git --version
```

## 2. GitHub runner agent — download and configure

Run these as the dedicated runner user (see [section 3](#3-runner-user-setup) if you haven't
created one yet).

```bash
mkdir -p ~/actions-runner && cd ~/actions-runner

# Get the latest runner package URL from:
# https://github.com/YOUR_GITHUB_REPO/settings/actions/runners/new
# (the version/checksum below will go stale — copy the current ones from that page)
curl -o actions-runner-linux-x64.tar.gz -L \
  https://github.com/actions/runner/releases/download/vX.Y.Z/actions-runner-linux-x64-X.Y.Z.tar.gz

tar xzf ./actions-runner-linux-x64.tar.gz

# Register this machine against your repo. Get a fresh registration token from:
# https://github.com/YOUR_GITHUB_REPO/settings/actions/runners/new
# (tokens expire quickly — generate one right before running this)
./config.sh \
  --url https://github.com/YOUR_GITHUB_REPO \
  --token YOUR_GITHUB_TOKEN \
  --name cachyos-runner \
  --labels self-hosted,cachyos,linux,x64 \
  --work _work
```

The `cachyos` label is what `.github/workflows/ci.yml` and `release.yml` target via
`runs-on: [self-hosted, cachyos]` — don't drop it from `--labels`, and don't reuse it on a runner
that isn't actually an Arch/CachyOS host with `yay`, since the workflows unconditionally set
`YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1` and assume that's safe on anything carrying this label.

## 3. Runner user setup

Don't run the runner as your everyday login user or as root. Create a dedicated, low-privilege
user, and grant it `sudo` for **only** the specific `pacman`/`yay` operations the build/test suite
and the app's own backend-install flow actually invoke — never a blanket `NOPASSWD: ALL`.

```bash
# As an existing admin user:
sudo useradd -m -s /bin/bash YOUR_RUNNER_USER
sudo passwd -l YOUR_RUNNER_USER   # no password login; runner service will run as this user directly

# Scoped sudoers rule — only what CI actually needs:
#   - pacman -S/-Syu/-Sy: package install/sync (build prerequisites, real-Arch integration tests)
#   - pacman -R/-Rns: uninstall (real-Arch integration tests)
# Edit with visudo, NOT a raw echo >> to /etc/sudoers — a syntax error there can lock out sudo
# entirely for every user on the box.
sudo visudo -f /etc/sudoers.d/yay-see-sharp-runner
```

Contents of `/etc/sudoers.d/yay-see-sharp-runner`:

```text
YOUR_RUNNER_USER ALL=(root) NOPASSWD: /usr/bin/pacman -S *, /usr/bin/pacman -Syu *, /usr/bin/pacman -Sy, /usr/bin/pacman -R *, /usr/bin/pacman -Rns *
```

```bash
sudo chmod 0440 /etc/sudoers.d/yay-see-sharp-runner
sudo visudo -c   # validates syntax across all of /etc/sudoers.d — run this after any manual edit

# Switch to the runner user for the rest of this guide:
sudo -iu YOUR_RUNNER_USER
```

## 4. First run — verify registration

Still as `YOUR_RUNNER_USER`, from `~/actions-runner`:

```bash
./run.sh
```

Watch the output for `Listening for Jobs`. In a browser, open
`https://github.com/YOUR_GITHUB_REPO/settings/actions/runners` — the runner should appear with a
green "Idle" status. Trigger the CI workflow (push a commit, or open a PR) and confirm a job picks
it up. Stop the foreground runner with `Ctrl+C` once confirmed — the systemd service in the next
section replaces this manual `run.sh` invocation for normal operation.

## 5. Systemd service — enable and start

The runner package ships its own systemd install script (as root, pointing at the runner user's
install directory):

```bash
exit   # back to your admin user, out of the YOUR_RUNNER_USER shell

cd /home/YOUR_RUNNER_USER/actions-runner
sudo ./svc.sh install YOUR_RUNNER_USER
sudo ./svc.sh start

# Verify:
sudo ./svc.sh status
systemctl status actions.runner.*
journalctl -u 'actions.runner.*' -f    # live logs; Ctrl+C to stop following
```

Confirm the runner shows "Idle" again on the GitHub runners page after the service starts — that
confirms it survives a reboot and isn't dependent on the manual `run.sh` session from step 4.

```bash
# Reboot test (optional but recommended once):
sudo reboot
# after it comes back up:
systemctl status 'actions.runner.*'   # should be active (running) with no manual step
```

## 6. Troubleshooting

**`dotnet: command not found` in workflow logs, but works in your interactive shell.**
The runner service starts with a minimal environment, not your login shell's `PATH`. Check where
the SDK actually installed (`which dotnet` as `YOUR_RUNNER_USER`) and either symlink it onto a
directory already on the service's `PATH` (commonly `/usr/local/bin`) or set `PATH` explicitly in
`~/actions-runner/.env` (the runner sources this file if present):

```bash
sudo -iu YOUR_RUNNER_USER
echo "PATH=$PATH" > ~/actions-runner/.env
sudo /home/YOUR_RUNNER_USER/actions-runner/svc.sh stop
sudo /home/YOUR_RUNNER_USER/actions-runner/svc.sh start
```

**`yay: command not found` only inside the gated integration test step.**
Same root cause as above (service `PATH` vs. login shell `PATH`) — fix via the same `.env` route.
Confirm with `sudo -u YOUR_RUNNER_USER yay --version` first to rule out yay not actually being
installed for that user at all.

**`sudo: a password is required` during the real-Arch integration tests.**
The sudoers rule in [section 3](#3-runner-user-setup) only covers the exact `pacman` invocations
listed. If `YayPackageBackend`/`YayBackendInstaller` changes what it runs (a new flag combination,
a different verb), the sudoers rule needs a matching update — check `sudo -l` as
`YOUR_RUNNER_USER` to see exactly what's currently permitted, and compare against what the failing
command actually tried to run (visible in the test output/log).

**Runner shows "Offline" on GitHub after a reboot.**
The systemd service likely isn't enabled (only started). Confirm with
`systemctl is-enabled 'actions.runner.*'`; if it prints `disabled`, re-run
`sudo ./svc.sh install YOUR_RUNNER_USER` from `~/actions-runner` (the install step both creates
*and* enables the unit).

**Registration token rejected / expired.**
Registration tokens from the GitHub UI are short-lived (typically ~1 hour). Generate a fresh one
from `https://github.com/YOUR_GITHUB_REPO/settings/actions/runners/new` immediately before running
`./config.sh`, not from a token you copied earlier in the session.

**Permission denied writing to the runner's `_work` directory.**
Usually means `svc.sh install` was run as a different user than the one that ran `config.sh`. Undo
with `sudo ./svc.sh uninstall`, then re-run `config.sh` and `svc.sh install` both as/for the same
`YOUR_RUNNER_USER`.
