# yay_see_sharp — Product Requirements

## Product goal

`yay_see_sharp` is a simple, fast Linux UI wrapper for the `yay` AUR helper. It should make package discovery and package maintenance easier than the existing graphical tools, with a clean interface and minimal unnecessary prompts.

## Supported platform behavior

- **Arch Linux / CachyOS:** Real mode using the installed `yay` command. The MVP's real backend is `yay`; other helpers such as `paru` are future extension points, not an MVP backend.
- **Ubuntu / Debian / other distributions:** Automatic Demo mode for the MVP. Demo data must be realistic, not random placeholder data. A future `apt` backend must be possible without changing the UI layer.
- Distribution detection is based on `/etc/os-release`.
- The active distribution, backend and mode are visible when the application starts.

## MVP capabilities

1. Search official packages and AUR packages.
2. Show package details: name, version, description when available, source, size, installation/update state and icon when available.
3. Filter results by `All`, `Official` and `AUR`.
4. Install packages.
5. Uninstall packages.
6. Show orphan dependencies before uninstall confirmation.
7. Remove orphan dependencies by default; allow disabling this in settings.
8. Check and apply updates.
9. Show installed-package statistics:
   - all installed packages,
   - explicitly installed packages,
   - dependency-installed packages,
   - AUR packages,
   - available updates,
   - disk space used,
   - orphan dependencies,
   - last update check.
10. Show operation progress and provide a hover popup with the current command, stages, live output, cancellation availability and final log.
11. Use realistic Demo mode operations on Ubuntu and other non-Arch systems without changing the host package database.
12. Offer installation of a missing recommended backend as an explicit, confirmed action. The MVP may offer the recommended path, but non-Arch systems remain Demo mode until a real apt backend is implemented.

## Security and privilege behavior

- Use `sudo -v` to obtain or refresh the sudo timestamp.
- Never store or encrypt the password.
- Never pass the password in command arguments or write it to logs.
- Ask again only when the sudo timestamp has expired.
- Show the command and request explicit confirmation before privileged actions.

## UI and UX

- Avalonia UI with MVVM.
- Dashboard is the startup screen.
- English, Slovak, German, Polish, Russian, Spanish, Portuguese, Italian, Chinese (Simplified and Traditional) and Japanese are supported.
- Language is selected from the system locale; unsupported locales fall back to English.
- Localization resources are externalized so additional languages can be added without changing application logic.
- System light/dark theme is followed automatically.
- Distribution, backend and real/demo mode are visible in the UI.
- The app can minimize to the system tray and restore its previous size, position and UI state.
- Closing the window defaults to hiding it in the tray; full exit is available from the tray menu. This behavior is configurable.
- Only one application instance may run. A second launch activates the existing instance.
- Desktop notifications are enabled by default and can be disabled in settings. Notifications cover available updates, successful install/uninstall, completed updates, operation errors and renewed sudo authorization.
- Desktop environment is detected and tray behavior is extensible. KDE Plasma is the primary real-system target; GNOME is the Demo target.

## Update schedule

- Check on startup.
- Automatic check once per day at a configurable time (default 10:00); the Settings screen's schedule description reflects the actual configured time, not a hardcoded interval.
- Screens auto-refresh after any operation that changes their data (install/uninstall/update) instead of requiring a manual Refresh button — see the UI/UX notes below.
- Notify when updates are available.

## UI/UX notes (superseding earlier assumptions above)

- No manual "Refresh" buttons on Dashboard or Installed — every screen refreshes itself automatically after the operation that would make its data stale (install, uninstall, update). This supersedes the "Manual `Refresh` action" line that appeared in earlier update-schedule requirements.
- **AUR helper build directory (`BuildDirectory` setting) is a future feature, not exposed in the current UI.** The underlying `SettingsViewModel.BuildDirectory` model field and `IBuildDirectoryPolicy` runtime wiring in `YayPackageBackend` (adds `--builddir` to install/update commands when configured) are kept intact so this can be reintroduced as a pure UI addition later, without a data-model change. Until then, it is always unset and has no effect on Install/Update behavior.
- Notifications are surfaced as in-app toasts (bottom-right, auto-dismissing after 30s) in addition to the OS-level desktop notification `notify-send` already covered above — the toast overlay is not gated by the desktop-notifications setting, since it's a different, always-on in-app surface.

## Quality requirements

- Follow MVVM and layered separation.
- Apply SOLID, DRY, KISS and YAGNI.
- Keep package backends behind interfaces so future `apt` and other helpers can be added without UI rewrites.
- Add TUnit unit tests for domain logic, parsing, detection, settings and Demo behavior.
- Add Demo integration tests for end-to-end UI-facing package workflows.
- Add a separately gated destructive Arch/CachyOS integration test using the `hello` package:
  search → install → verify installed → verify files → uninstall → verify absent → verify files removed.
- Destructive tests must never run automatically on Ubuntu or an unsupported host.
