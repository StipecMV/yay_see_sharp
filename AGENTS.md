# AGENTS.md — Yay See Sharp

> Návod pre AI agentov a prispievateľov pracujúcich v tomto repozitári.
> Tento súbor sa automaticky vkladá do kontextu agenta (Project context) — čítaj ho PRED akoukoľvek prácou v tomto projekte.

## 1. Čo je tento projekt

**Yay See Sharp** je rýchly, minimalistický desktopový GUI wrapper pre `yay` (AUR helper) na Arch Linuxe / CachyOS — vyhľadávanie, inštalácia, aktualizácia a odinštalovanie balíkov bez terminálu.

- **Stack:** .NET 10, Avalonia UI 12.x (MVVM), TUnit + Moq, ReactiveUI
- **Licencia:** GPL-3.0
- **Repo:** `github.com/StipecMV/yay_see_sharp` — vetva `main` (komituje sa priamo na main)
- **Riešenie:** `yay_see_sharp.slnx` (`.slnx`, nie klasický `.sln`)
- **Dokumentácia:** `README.md` (aktuálny stav + screenshoty), `docs/architecture.md`, `docs/product-requirements.md`, `docs/aur-packaging-guide.md`, `docs/bugfixes-2026-08.md`, `docs/setup-self-hosted-runner.md`, `tools/README.md`

## 2. Build a testy (POVINNÉ príkazy)

```bash
# Build celej solution — musí byť 0 chýb (0 warningov je cieľ)
dotnet build yay_see_sharp.slnx

# Testy — TUnit: POUŽI dotnet run --project, NIE dotnet test (MTP/.NET 10)
for p in domain infrastructure application integration e2e; do
  dotnet run --project tests/yay_see_sharp.$p.Tests
done

# Gated Arch/CachyOS integračné testy (destruktívne! len na Arch s yay)
YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1 dotnet run --project tests/yay_see_sharp.integration.Tests

# Regenerácia screenshotov do README (Avalonia headless/X11, Xvfb ak niet DISPLAY)
./tools/generate-screenshots.sh [--theme dark] [--lang sk]
```

**Aktuálny stav (2026-08):** 290/290 testov zelených — 7 domain + 166 infrastructure + 105 application + 12 e2e.
**Screenshoty** sa generujú z reálne skompilovanej aplikácie (Demo backend), nie ručne.

## 3. Architektúra (striktne jednosmerné závislosti)

```text
yay_see_sharp.application  (Avalonia Views, ViewModels, App shell, AppBootstrapper = composition root)
        ↓                    ↓
yay_see_sharp.infrastructure  (YayPackageBackend, DemoPackageBackend, HTTP, súbory, sudo, notifikácie, scheduler)
        ↓
yay_see_sharp.domain       (modely + interfacy — ŽIADNE third-party závislosti, len BCL)
```

- **UI NIKDY nespúšťa `yay`/`pacman`/`apt` priamo** — každá operácia ide cez `IPackageBackend` (rovnaké ViewModely jazdia Real aj Demo backend).
- **Real mode:** Arch/CachyOS s `yay` → `YayPackageBackend` (cez `ICommandRunner`, nikdy shell string).
- **Demo mode:** všetko ostatné → `DemoPackageBackend` (realistická in-memory simulácia, nikdy nesiahne na hostiteľa). Detekcia cez `/etc/os-release`.
- **Lokalizácia:** dictionary `Resources/LocalizationResources.cs` (11 jazykov: EN/SK/DE/PL/RU/ES/PT/IT/zh-CN/zh-TW/JA), **nie `.resx`** — live prepínanie jazyka cez `LocalizedViewModelBase`. Dynamický obsah z backendu (názvy balíkov) sa neprekladá.
- Kľúčové interfacy: `IPackageBackend`, `ICommandRunner`, `IDistributionDetector`, `IPrivilegeService`, `ISettingsStore`, `ILocalizationService`, `INotificationService`, `ITrayService`, `ISingleInstanceService`, `IClock`.

## 4. Čo sme riešili (história)

- **2026-08 bugfix runda** (`0b24a06`) — 13 problémov z testovania Real mode na živom CachyOS: falošný „Saved" toast pri otvorení Settings, 30s→10s toast, Detect hlásil „Saved" namiesto výsledku, vertikálne filtre, stratený filter v Installed, orezané texty po zdvojnásobení fontu, nespoľahlivý `[installed]` marker z yay (fix: krížová kontrola `pacman -Qq`), PKGBUILD modal mimo okna, pomalé prepínanie filtrov (fix: okamžité vyčistenie + spinner), chýbajúca selekcia v Search, dvojité popupy pri chybe (fix: OS notifikácie OFF default), htop z Search neviditeľný v Installed (fix: refresh pri navigácii), zlý AUR count + „Updates available" (fix: chunked `yay -Si` ≤20 mien ≤4 concurrent). Detaily: `docs/bugfixes-2026-08.md`.
- **2026-08 follow-up** — CS0618 warning v Release (TUnit `HasCount()` → `Count().IsEqualTo()`); AUR konfirmácia parsuje chunk výstup aj pri exit code ≠ 0 (jeden „not in AUR" názov už nezhodí celý chunk → Installed AUR filter aj Dashboard AUR count správne). 285/285 testov.
- **Logovanie (log4net)** — súborový logger pre celú appku (infra aj application vrstva): per-run súbor `~/.config/yay_see_sharp/logs/yay-see-sharp-<start>-<pid>.log`, 10 MB na segment, max 2 súbory na beh (pri 3. segmente sa maže najstarší); logujú sa príkazy yay/pacman (exit code, trvanie, tail pri chybe), výber backendu, settings, operácie, výnimky; Splat/ReactiveUI logy tiež do súboru; log4net konfigurácia v kóde (`Logging/LoggingSetup.cs`), žiadny XML config.
- **Paru engine (PARU-2026-08)** — paru je plnohodnotný backend: `YayPackageBackend` parameterizovaný executable (yay/paru — rovnaký CLI povrch, žiadna duplicita), `PackageBackendFactory` číta preferenciu cez `IEnginePreference` (SettingsViewModel), EngineOptions = yay+paru, persistovaná Paru preferencia sa už **neclampne** (regresný test nahradený testom zachovania), Detect aplikuje nájdený engine (aj paru), chýbajúci preferovaný engine = Unavailable (nie tichý fallback) s lokalizovanou výstrahou (`Dashboard.ParuMissingWarning`, 11 jazykov); logy/display stringy používajú reálny executable. Testy: factory (paru Real/Unavailable/default yay), backend cez paru executable, Settings (options, persist, detect paru).
- **Lokalizácia DE/PL** — pridané nemecké (de) a poľské (pl) jazykové sady (126 kľúčov každá, plná parita); jazyk sa prepína v Settings ako EN/SK. Testy: `LocalizationServiceTests` (de/pl/fallback), `SettingsViewModelTests` (možnosti jazyka).
- **Lokalizácia RU/ES/PT/IT/zh-CN/zh-TW/JA** — pridaných 7 jazykových sád (133 kľúčov každá, plná parita generovaná + overená); spolu 11 jazykov. Testy: `LocalizationServiceTests` (7 nových setov), `SettingsViewModelTests` (11 možností jazyka).
- **Code review findings** (52beea1) — findings z review zapracované; dokument `docs/code-review-findings.md` bol po zapracovaní **odstránený** (ed2ee29), aktuálne žiadny takýto súbor neudržiavaj.
- **Packaging guide** — `docs/aur-packaging-guide.md` (PKGBUILD runbook, framework-dependent build s `dotnet-runtime` v `depends`); **balík do AUR ešte nebol odoslaný**.
- **Self-hosted CI runner** — `docs/setup-self-hosted-runner.md`.
- UI zmeny: škálovanie prvkov na dvojnásobný font (c3966c6), odstránenie blogu/design handoff (ed2ee29).

## 5. SCHVÁLENÉ rozhodnutia (drž sa ich)

| Rozhodnutie | Stav |
|---|---|
| MVP backend = `yay`; `paru` = rovnocenný engine (2026-08: parameterizovaný backend, preferencia v Settings) | ✅ potvrdené |
| `DemoPackageBackend` na Ubuntu/Debian/iné; budúci `apt` backend bez zmeny UI vrstvy | ✅ potvrdené |
| Auto-detekcia backendu; nepodporovaný host NIKDY ticho nevykonáva Arch príkazy | ✅ potvrdené |
| Lokalizácia cez dictionary, nie `.resx` | ✅ potvrdené |
| OS-level notifikácie (notify-send) **OFF by default** (opt-in v Settings); in-app toasty vždy zapnuté, auto-dismiss **10 s** | ✅ po bugfix runde |
| Auto-save v Settings: debounced 250 ms + diff proti poslednej uloženej hodnote → „Saved" toast LEN pri reálnej zmene | ✅ po bugfix runde |
| Engine výber (yay/paru) v Settings: perzistentný; Real mode beží na preferovanom engine; chýbajúci preferovaný engine = Unavailable (nie tichý fallback na druhý) | ✅ 2026-08 |
| Súborový logger: log4net (per-run súbor, 10 MB segment, max 2 súbory/beh), `~/.config/yay_see_sharp/logs/` | ✅ 2026-08 |
| Žiadne manuálne „Refresh" buttony — obrazovky sa auto-refreshujú po operácii a pri navigácii | ✅ potvrdené |
| Sudo: `sudo -v` refresh, heslo NIKDY v argv, logoch ani na disku; potvrdenie pred privilegovanými akciami | ✅ potvrdené |
| Close = hide do tray (konfigurovateľné); single instance (druhý štart aktivuje existujúcu) | ✅ potvrdené |
| KDE Plasma = primárny real cieľ; GNOME = Demo target | ✅ potvrdené |
| Update check: pri štarte + denne o konfigurovateľnom čase (default 10:00) | ✅ potvrdené |
| Testy: TUnit + Moq; mocks len na hraniciach (ICommandRunner, HTTP); destruktívne Arch testy cez env flag | ✅ potvrdené |

## 6. ZAMIETNUTÉ / odložené rozhodnutia (NEVRAČAJ to späť bez diskusie so stakeholderom)

- **In-app instalátor pre `paru`** — odložené: instalátor backendu ostáva yay-only (prompt texty sú yay-špecifické); pri chýbajúcom preferovanom paru sa ukáže lokalizovaná výstraha (`Dashboard.ParuMissingWarning`), paru sa nainštaluje `sudo pacman -S paru` (je v extra repo).
- **`apt` backend v MVP** — ZAMIETNUTÉ (future; na Ubuntu ostáva Demo mode).
- **AUR helper build directory (`--builddir`) v UI** — ZAMIETNUTÉ pre MVP: model (`SettingsViewModel.BuildDirectory`) a `IBuildDirectoryPolicy` v `YayPackageBackend` zostávajú nedotknuté, ale **nie je žiadny Settings ovládací prvok**; vždy unset, žiadny efekt. Zaviesť len ako čistý UI addition neskôr.
- **OS-level notifikácie ako default** — ZAMIETNUTÉ (dvojité popupy systém + in-app).
- **Manuálne Refresh buttony** — ZAMIETNUTÉ (auto-refresh po operáciách).
- **`.resx` lokalizácia** — ZAMIETNUTÉ (satellite assembly problémy v unit testoch).
- **Fixný toast 30 s** — ZAMIETNUTÉ (10 s).
- **Destruktívne testy na ne-Arch hostoch / v default suite** — NIKDY automaticky.
- **AUR balík odoslať** — ešte neodoslané; `aur-packaging-guide.md` je len runbook.

## 7. Pravidlá práce v tomto repozitári

1. **Pred zmenou:** prečítaj `docs/product-requirements.md` + `docs/architecture.md` (sú authority pre požiadavky a štruktúru).
2. **Scoped zmeny:** drž zmenu v jednej architektonickej vrstve, kde sa dá; testy pridávaj do zodpovedajúceho test projektu (domain→domain.Tests, atď.).
3. **Po zmene:** `dotnet build yay_see_sharp.slnx` (0 chýb) + spustiť aspoň dotknuté test projekty. Pri UI zmenách regenerovať screenshoty.
4. **Real mode veci** (tray, OS notifikácie, sudo dialog, reálne yay operácie) sa v CI/testoch **nedajú overiť** — označ ich ako vyžadujúce manuálne overenie na Arch/CachyOS, netvár sa že sú overené.
5. **Jazyk UI:** 11 jazykových sád (EN, SK, DE, PL, RU, ES, PT, IT, zh-CN, zh-TW, JA); nové reťazce pridávaj do VŠETKÝCH sád (parita kľúčov je testovaná).
6. Commit message štýl: krátke popisné (`fix: ...`, `feat: ...`); commity priamo na `main`.
7. **Rozhodnutia** o zmene správania (schválené/zamietnuté) sa zaznamenávajú do tohto súboru (sekcie 5/6) a do Hindsight (todo board „Yay See Sharp projekt").

## 8. Známé muchy a pitfalls

- `dotnet test` nefunguje spoľahlivo pre TUnit projekty — vždy `dotnet run --project`.
- `dotnet run --project ... --no-build` môže bežať zo STALE binárok v `output/bin/` (starý layout) namiesto `output/bin/Debug/` — ak výsledky nezodpovedajú zdrojáku, spusti testy BEZ `--no-build`.
- Filter bugy: sleduj `SelectedSourceOption` (notifikuje), NIE `SourceFilter` (nikdy nevyhodí PropertyChanged).
- `pacman -Qq` je rýchly lokálny zdroj stavu „installed" — používa sa na krížovú kontrolu.
- AUR potvrdenia (`yay -Si`) chunkuj (≤20 mien, ≤4 concurrent) — jeden neriešiteľný názov zhodí celé volanie.
- Avalonia load-time binding pushy (napr. ComboBox "" → hodnota) spúšťajú settery — auto-save diff ich už ignoruje.
- Settings Detect: vždy toast „Detection result", nikdy nevyvolá save.
