# Bugfix round — 2026-08 (live CachyOS feedback)

All issues below were reported by the stakeholder after exercising the **Real mode** backend on a live
CachyOS desktop (2026-08): installing/uninstalling packages, switching filters, browsing Search /
Installed / Settings. Every fix is covered by a unit or headless E2E regression test (273 tests
passing after this round).

| # | Reported symptom | Root cause | Fix |
| --- | --- | --- | --- |
| 1 | Opening Settings shows a "Saved/Uložené" toast even though nothing was changed | Every settings setter called `TriggerAutoSave()` unconditionally — Avalonia's load-time binding pushes (e.g. the Language ComboBox briefly writing `""` then the real value) wrote settings back and toasted | Auto-save is now a debounced (250 ms) pipeline that diffs the current values against the last-persisted snapshot and skips the write + toast when nothing actually changed; the Language setter ignores the empty-string initialization push. Regression tests: `SettingsViewModelTests` + `SettingsE2ETests.Opening_settings_without_changes_shows_no_saved_toast` / `..._preserves_a_non_default_theme_without_saving` |
| 2 | The "Saved" toast lingers ~1 minute | In-app toasts auto-dismissed after 30 s (and stacked toasts felt longer) | Auto-dismiss shortened to **10 s** (`ToastService`) |
| 3 | Clicking **Detect** in Settings shows "Saved" instead of what was found | Detect applied the engine silently; the auto-save machinery then toasted "Saved" | Detect now always reports a **"Detection result"** toast: yay found / paru found (not supported yet) / nothing found — and never triggers a save. New localization keys `Settings.DetectResult*` (EN + SK) |
| 4 | Appearance / Search / Installed filter options stack vertically instead of side by side | Segmented ListBoxes relied on the theme's ItemsPanel, which didn't apply reliably | Each segmented ListBox now declares its horizontal `ItemsPanel` inline (Settings theme + engine, Search filter, Installed filter) |
| 5 | Installed: after searching, clearing the text never shows the installed apps again | The filter subscription watched the computed `SourceFilter` property, which never raises `PropertyChanged` — a filter click (e.g. Official/AUR) silently stuck and broke the subsequent search/clear | Search + Installed now observe `SelectedSourceOption` (which notifies). Installed applies filter clicks immediately; typing stays debounced. Regression: `InstalledPackagesViewModelTests.Clearing_the_query_restores_all_packages` |
| 6 | Many texts are cut off after the UI font was doubled | Fixed-size layouts: dashboard stat cards `Height=104`, Installed rows `"*,64,52,44"`, no trimming on names/versions | Cards grow with content (`TextWrapping`), row columns are content-sized (`Auto`) with `TextTrimming="CharacterEllipsis"` on names/versions/details; dashboard/update rows trim too |
| 7 | firefox shows "Install" in Search but "installed" in Installed | Search state came only from yay's `[installed]` marker, which is unreliable across yay versions | `YayPackageBackend.SearchAsync` cross-checks `pacman -Qq` (fast local query, run in parallel) and marks matches as `Installed`; parser unit tests added for the marker itself |
| 8 | View PKGBUILD is barely visible when the window isn't maximized | The modal had a fixed `Width=960`, overflowing smaller windows | Modal now sizes to the available space (`MaxWidth/MaxHeight` + margins, centered); inner code block scrolls |
| 9 | Switching filters reacts late / stale rows linger ("firefox stayed AUR"); no feedback while loading | Filter changes didn't trigger re-search (same `SourceFilter` non-notification bug); results only refreshed when the backend replied | Filter switches now clear the stale rows immediately and show an **indeterminate progress bar** while the search is in flight (`IsBusy`); Search + Installed views both got the spinner |
| 10 | Search: selecting a package doesn't stay selected; unclear which row the detail pane belongs to | Results were an `ItemsControl` of buttons — no selection model at all | Results are now a real `ListBox` bound to `SelectedPackage`, with a visible selected-row highlight (`Controls.axaml`), and the selection is re-applied by name after every live-search reload (`RepopulateResults`) |
| 11 | VLC install failure: two popups (system + in-app), and "Installation failed with exit code 1" says nothing | `CompositeNotificationService` fanned every event to OS notifications too; failure messages carried no output | Desktop (OS-level) notifications are **off by default** (Settings toggle re-enables them); install/uninstall/update failure messages now append the last meaningful lines of the process output (`FormatFailure`) |
| 12 | htop installed from Search never appears in Installed | Installed only auto-refreshed on its own detail-pane operations — installs from Search were invisible | Navigating to Installed (or Dashboard) now refreshes the screen's data (`MainWindowViewModel`), covering every mutation path including changes made outside the app |
| 13 | Dashboard shows wrong AUR count and wrong "Updates available" | AUR count came from one giant `yay -Si <all foreign names>` call (fragile — one unresolvable name failed it all); the updates card counted `pacman -Qu` (repo-only) while the list used `yay -Qu` (repo + AUR) | AUR confirmation queries are chunked (≤20 names, ≤4 concurrent) and merged per-chunk; the "Updates available" card now mirrors the count of the update list actually rendered below it. Regression: `DashboardViewModelTests.Updates_available_statistic_matches_the_rendered_update_list` |

## Files touched

- `source/yay_see_sharp.application/ViewModels/SettingsViewModel.cs` — debounced diff-based auto-save, Detect result toasts, setter guards
- `source/yay_see_sharp.application/ViewModels/SearchViewModel.cs` — `SelectedSourceOption` reactivity, immediate stale-result clear, selection preservation
- `source/yay_see_sharp.application/ViewModels/InstalledPackagesViewModel.cs` — filter reactivity, public `RefreshAsync`
- `source/yay_see_sharp.application/ViewModels/DashboardViewModel.cs` — updates count consistency, public `RefreshAsync`
- `source/yay_see_sharp.application/ViewModels/MainWindowViewModel.cs` — refresh-on-navigation
- `source/yay_see_sharp.application/Platform/ToastService.cs` — 10 s auto-dismiss
- `source/yay_see_sharp.application/Themes/Controls.axaml` — selected-row highlight styles
- `source/yay_see_sharp.application/Views/*.axaml` — horizontal segmented controls, spinners, dynamic layout, responsive PKGBUILD modal, trimming
- `source/yay_see_sharp.domain/Models/SettingsModels.cs` — `NotificationsEnabled` default → false
- `source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.cs` — `pacman -Qq` search-state cross-check, failure detail tail
- `source/yay_see_sharp.infrastructure/Yay/PacmanQueryService.cs` — chunked AUR confirmation
- `source/yay_see_sharp.infrastructure/Resources/LocalizationResources.cs` — `Settings.DetectResult*` (EN + SK)
- Tests: `SettingsViewModelTests`, `SearchViewModelTests`, `InstalledPackagesViewModelTests`, `DashboardViewModelTests`, `PackageBackendTests`, `SettingsE2ETests`

## Verification

```bash
dotnet build yay_see_sharp.slnx
for p in domain infrastructure application e2e; do
  dotnet run --project tests/yay_see_sharp.$p.Tests
done
```

Result after this round: **273/273 passing** (7 domain, 150 infrastructure, 104 application, 12 e2e).
Screenshots in `docs/screenshots/` were regenerated from the fixed UI via `tools/generate-screenshots.sh`.

## Follow-up (same day)

Two issues surfaced when re-checking the Release build and the Installed screen on the live box:

| # | Reported symptom | Root cause | Fix |
| --- | --- | --- | --- |
| 14 | Release build warns `CS0618`: `HasCount()` is obsolete | `SettingsViewModelTests.Language_options_are_built_from_available_languages` used the legacy TUnit `HasCount().EqualTo(11)` API | Migrated to the modern `Count().IsEqualTo(11)` — `dotnet build -c Release` is now **0 warnings** |
| 15 | Installed → **AUR** filter still shows 0 packages (and Dashboard AUR count 0) even though many AUR packages are installed | Follow-up on #13: `yay -Si` exits **non-zero when any requested name is unresolvable** (e.g. a foreign, hand-built package that isn't in the AUR) — but still prints the info blocks for every name it *did* resolve. The chunked confirmation only parsed output when `result.Succeeded`, so **one bad name silently discarded the whole chunk** → AUR packages were classified `Foreign` → invisible under the AUR filter and absent from the count | Confirmation now parses the chunk output **regardless of the exit code** (`ParseAurConfirmedNames` already skips error lines); a chunk with no resolvable blocks contributes nothing. Regression: `PackageBackendTests.Yay_get_installed_classifies_aur_packages_even_when_the_confirmation_chunk_exits_nonzero` |

Verification after the follow-up: **285/285 passing** (7 domain, 162 infrastructure, 104 application, 12 e2e).
