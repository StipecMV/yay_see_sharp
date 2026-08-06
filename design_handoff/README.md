# Handoff: Yay See Sharp — Desktop UI

## Overview
UI mockups for "Yay See Sharp", a graphical front-end for `yay` (AUR helper) on Arch Linux. Target implementation: **Avalonia UI (C#, XAML)**, native window chrome (no custom titlebar — window controls in the mockups are illustrative only, use the OS/DE default).

> **Implementation note:** the mockups below show a `yay`/`paru` engine picker. Only `yay` is actually implemented today — there is no `ParuPackageBackend`, so the running app's Settings screen offers `yay` alone (see `docs/implementation-status.md`, "Engine picker"). Treat every `paru` reference below as a **future feature**, not something to wire up a non-functional UI toggle for again.

## About the Design Files
The bundled file (`design.html`) is an **HTML design reference** — a static prototype showing intended look, layout and states. It is not code to port directly. Recreate these screens as Avalonia Views/UserControls (XAML + C#) using Avalonia's styling system (Styles/ControlThemes, `DynamicResource` for theme colors), not by embedding HTML/WebView.

## Fidelity
**High-fidelity.** Colors, spacing, radii and type sizes below are final; treat exact values as authoritative when translating to XAML.

## Screens / Views
1. **Loading / splash** — centered logo mark, app name, thin progress bar, status text ("Syncing AUR metadata…").
2. **Dashboard** — sidebar nav + main content: dismissible in-app notification banner, welcome header, 3-stat row (Installed / From AUR / Updates), "Updates available" list with per-row Update, "Update all" button.
3. **Search** — sidebar + search field, result rows (icon, name, repo tag, description, votes, version, Install button — or an "Installed" tag if already installed).
4. **Installed** — sidebar + package table-like list (name, version, size, repo tag) + a **build progress modal** overlay (title, scrollable monospace log, progress bar, "Run in background").
5. **Package detail** — icon, name/version, Installed tag, description, meta grid (Maintainer, Votes, Install size, Updated), dependency tags, Uninstall / View PKGBUILD buttons.
6. **Settings** — sidebar + Appearance (Dark/Light segmented toggle), Language (dropdown-style field, English default, Slovenčina supported, designed to scale to many languages later — do not use a fixed segmented control), Package manager engine (yay/paru segmented + "Detect" button), AUR helper Build directory (custom in-app folder browser, see screen 9 — not the native OS file dialog), toggle switches (auto-update check, desktop notifications, minimize to tray).
7. **Password prompt** — small modal, lock icon, "Authentication required" title, explanatory copy, password field, Cancel/Authenticate.
8. **Tray icon** — single static monogram icon in the system tray; clicking it opens the main window (no dropdown menu).
9. **Folder browser (custom)** — modal triggered by Settings' "Browse…": breadcrumb path, scrollable folder list (selected row tinted + checkmark), resulting path field, Cancel / Select folder.

### Layout notes
- App shell: fixed-width left sidebar (140px) + flexible content, standard for all main screens (Dashboard/Search/Installed/Settings).
- Sidebar always starts with the brand mark + "Yay See Sharp" wordmark, then 4 nav items with icons (Dashboard = 2×2 grid glyph, Search = magnifier, Installed = box glyph, Settings = gear glyph), Settings pinned to the bottom via `margin-top:auto` equivalent (e.g. a `Grid` row with `*` then auto, or a bottom-docked item).
- Card/list rows use 8px radius, subtle row background tint, no heavy borders.
- Modals: dark scrim backdrop, surface-colored panel, 14px radius, top-elevation shadow.

## Interactions & Behavior
- Theme toggle (Dark/Light) is global — every screen re-themes live; implement as an app-level resource dictionary swap (e.g. Avalonia `ThemeVariant`/merged `ResourceDictionary`), not per-view logic.
- Dashboard notification banner: dismissible via the × button (removes from view).
- Installed screen build modal: "Run in background" or × collapses the modal without cancelling the build (it should keep running; represent progress state at the app level, e.g. a build queue/status service).
- Search rows: conditionally render "Install" button vs "Installed" tag based on package install state.
- Settings → Build directory row is a full-width clickable field ("Browse…") that opens the **custom in-app folder browser** (screen 9) — do not shell out to the native GTK/Qt file picker, to keep visual consistency.
- Settings → "Detect" button next to the engine picker re-runs engine detection (yay/paru presence check) and updates the selected segment.
- Password prompt and build-progress modal are app-modal (block interaction with the main window behind them).

## State Management
Suggested state/view-models:
- `ThemeService` — current theme (Dark/Light), persisted to user settings.
- `PackageListViewModel` — installed packages, available updates, search results (name, version, repo, size, votes, description, dependencies, installed flag).
- `BuildJobViewModel` — current build: package name, log lines, progress %, step label; supports "run in background" (minimize modal, keep job alive).
- `SettingsViewModel` — language, detected/selected engine (yay/paru), build directory path, auto-update-check toggle, notifications toggle, minimize-to-tray toggle.
- `AuthPromptViewModel` — triggered whenever an operation needs elevated privileges; resolves/rejects a pending privileged action.

## Design Tokens
Derived from the bound "Nocturne" design system (mono-accent, dark-first; a light variant was authored to match).

**Dark theme**
- Background (page): `#0e0f18`; Surface (cards/panels): `#232532`; Sidebar/titlebar: `#1c1e2b`
- Text: `#e9e9ed`; Text muted: `rgba(233,233,237,0.62)`; Text faint: `rgba(233,233,237,0.42)`
- Divider: `rgba(233,233,237,0.14)`
- Accent: `#9184d9` (borders, icons, active nav, links); Accent tint background: `rgba(145,132,217,0.16)`
- Error/destructive text (Uninstall): `#d99b91`
- Tag neutral bg/fg: `rgba(233,233,237,0.09)` / `rgba(233,233,237,0.7)`; Tag accent bg/fg: `#423a6a` / `#d2cefd`
- Shadows: `0 0 0 1px #3f424d, 0 6px 18px rgba(0,0,0,0.5)` (md), `0 0 0 1px #3f424d, 0 16px 40px rgba(0,0,0,0.6)` (lg)

**Light theme**
- Background: `#eeecf6`; Surface: `#fdfcff`; Sidebar/titlebar: `#f1eff9`
- Text: `#232332`; Text muted: `rgba(35,35,50,0.62)`; Text faint: `rgba(35,35,50,0.42)`
- Divider: `rgba(35,35,50,0.12)`
- Accent: `#6d61b8`; Accent tint background: `rgba(109,97,184,0.10)`
- Error/destructive text: `#a8493a`
- Tag neutral bg/fg: `rgba(35,35,50,0.06)` / `rgba(35,35,50,0.65)`; Tag accent bg/fg: `#e7e5fe` / `#5d5294`

**Shared**
- Font: Inter (400/500/600/700), monospace accents (package versions/paths/log) in `ui-monospace`/system monospace.
- Radius: 8px (controls/rows/tags smaller variant), 14–16px (cards/modals/logo mark).
- Spacing: compact — 6–12px for row padding, 16–24px for section/panel padding.
- Buttons: outlined only (1px accent border, transparent fill) for primary actions — never a solid accent fill.

## Assets
- **Logo/monogram**: a bold "y" glyph (main text color) with a small 4-line geometric hash mark (accent-colored, drawn as SVG lines — not the literal "#" character) overlapping the bottom-right corner of a bordered rounded-square badge. Used at 3 sizes: 60px (loading screen), 24px (sidebar brand row), 18px (tray icon). Recreate as a small vector asset (SVG/XAML `Path`/`Geometry`) rather than a font glyph, so it renders crisply at tray-icon size.
- **Package "icons"**: letter-monogram placeholders (2-letter initials on a tinted rounded square) — stand-ins until real package icons/screenshots are wired up.
- Nav icons (grid/magnifier/box/gear) and folder icon are simple stroke-based glyphs; recreate as vector icons (e.g. via an icon font/SVG resource) at 14px.

## Files
- `design.html` — the full mockup (all 9 screens, live Dark/Light toggle). Open directly in a browser to review.
