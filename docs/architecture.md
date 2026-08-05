# yay_see_sharp — Prototype Architecture

## Architecture style

Use a small layered architecture with MVVM at the UI boundary:

```text
Avalonia Views
    ↓ bindings/commands
ViewModels
    ↓ application services
Application layer
    ↓ interfaces
Domain models + backend contracts
    ↓ implementations
Infrastructure: yay process runner, distro detection, Demo data, settings, sudo, tray
```

The UI must not invoke `yay`, `pacman`, `apt` or shell commands directly.

## Core abstractions

- `IPackageBackend`: search, details, installed packages, updates, install, uninstall and statistics.
- `ICommandRunner`: controlled process execution with streamed output, cancellation and exit status.
- `IDistributionDetector`: reads `/etc/os-release` and reports distribution, desktop environment and session type.
- `IPrivilegeService`: checks/refreshes `sudo -v` without storing the password.
- `ISettingsStore`: persists user settings independently of the UI.
- `ILocalizationService`: resolves localized strings with system-locale detection and English fallback.
- `INotificationService`: desktop notifications, enabled by default.
- `ITrayService`: tray menu, hide/restore and exit.
- `ISingleInstanceService`: single-instance lock and activation message.
- `IClock`: injectable time source for update scheduling and deterministic tests.

## Backend strategy

- `YayPackageBackend` is the real MVP backend for Arch/CachyOS.
- `DemoPackageBackend` supplies a realistic in-memory catalog and stateful install/uninstall/update simulation for Ubuntu, Debian and other distributions.
- Future `AptPackageBackend` can implement the same `IPackageBackend` without changing ViewModels or Views.
- Backend selection is automatic from distribution detection. Unsupported hosts never silently execute Arch commands.

## UI structure

- `MainWindowViewModel`: dashboard state, navigation and global mode indicator.
- `SearchViewModel`: query, source filter, results and selected package.
- `PackageDetailsViewModel`: package metadata and install/uninstall actions.
- `StatisticsViewModel`: installed package counters and last refresh.
- `UpdatesViewModel`: update list, schedule and refresh action.
- `OperationViewModel`: progress, stages, live output and operation result.
- `SettingsViewModel`: language, helper priority/selection, orphan removal, notifications, close behavior and schedule.

Views should remain declarative and bind to ViewModels. Commands and services are testable without starting the GUI.

## Localization

Implemented as `Resources/LocalizationResources.cs`: an `internal static` EN/SK key→string dictionary (not `.resx`, to avoid satellite-assembly resolution issues in unit tests and keep resources testable without touching disk). `ILocalizationService`/`LocalizationService` wrap it, expose `GetString(key)`, `AvailableLanguages`, `SetLanguage(...)` and a `LanguageChanged` event. Add a new language by adding a new key set to the dictionary; business logic remains unchanged.

ViewModels that expose localized text derive from `LocalizedViewModelBase`, which subscribes to `LanguageChanged` and calls an overridden `RaiseLocalizedPropertiesChanged()` to re-notify bound computed string properties — this is what makes language switching apply live, without restarting the app, as long as all ViewModels sharing one `MainWindowViewModel` tree use the same `ILocalizationService` instance (wired once in `App.axaml.cs`). Enum-backed selections (theme, close action, language, package source filter) expose `SelectableOption<T>` lists with a localized `Label` instead of relying on `.ToString()`.

Scope boundary: backend-origin dynamic content (package names/descriptions, raw exception messages) is not translated — only static UI chrome text is.

## Settings defaults

- Language: system locale, English fallback.
- Close action: hide to tray.
- Notifications: enabled.
- Remove orphan dependencies: enabled.
- Update schedule: daily at 10:00.
- Helper selection: automatic; on MVP real hosts only `yay` is supported.
- Theme: follow system.

## Test layers

1. **Unit tests:** pure domain, parsing, platform detection, demo backend state transitions, scheduling and settings.
2. **Demo integration tests:** use `DemoPackageBackend`, install/uninstall and verify state/file simulation without touching the host.
3. **Arch integration tests:** explicit opt-in only, require `yay`, Arch/CachyOS and a test flag; use `hello` and verify package state and files.
4. **UI smoke verification:** launch Ubuntu Demo mode, verify dashboard/search/settings/operation views and capture a screenshot.

## Implementation sequence

1. Requirements and acceptance criteria.
2. Domain contracts and models.
3. Distribution detection and backend selection.
4. Demo backend and real yay backend.
5. Application services and settings.
6. MVVM dashboard and operation UX.
7. Tray, single instance, notifications and localization.
8. Unit tests.
9. Demo and gated Arch integration tests.
10. Build, run, screenshot and review.
