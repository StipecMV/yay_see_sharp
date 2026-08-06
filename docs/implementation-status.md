# yay_see_sharp — Implementation Status

> Tento dokument popisuje **skutočný stav behu kódu**, nie plán. Kde je funkcia len čiastočná, je to explicitne uvedené aj s tým, čo chýba. Automatizovaná verifikácia (build + testy) je striktne oddelená od manuálnej GUI/real-Arch verifikácie — druhá menovaná nebola v tomto sandboxe vykonaná (bez X11/Wayland, bez `notify-send`/D-Bus session, bez reálneho Arch/CachyOS hosta).

**Dátum poslednej verifikácie:** 2026-08-06.

## Posledný build/test stav (automatizovaná verifikácia)

```bash
dotnet restore yay_see_sharp.slnx
dotnet build yay_see_sharp.slnx --configuration Debug
dotnet build yay_see_sharp.slnx --configuration Release
dotnet run --project tests/yay_see_sharp.domain.Tests
dotnet run --project tests/yay_see_sharp.infrastructure.Tests
dotnet run --project tests/yay_see_sharp.application.Tests
dotnet run --project tests/yay_see_sharp.integration.Tests
dotnet run --project tests/yay_see_sharp.e2e.Tests
```

- **Build:** Debug aj Release — 0 warnings, 0 errors, celé riešenie (3 source assembly: `yay_see_sharp.domain`, `yay_see_sharp.infrastructure`, `yay_see_sharp.application`; 5 test projektov nižšie). Debug a Release teraz buildujú do oddelených `output/bin/$(Configuration)/` priečinkov (NEW-08) — predtým zdieľali jeden priečinok, takže `dotnet run --no-build` po Release builde v skutočnosti spúšťal Release binárky pod menom "Debug".
- **Testy — spolu 262: 259 passed, 0 failed, 3 skipped** (skipnuté = gated real-Arch integration testy, pozri nižšie).

| Projekt | Príkaz | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- | --- |
| `tests/yay_see_sharp.domain.Tests` | `dotnet run --project tests/yay_see_sharp.domain.Tests` | 7 | 7 | 0 | 0 |
| `tests/yay_see_sharp.infrastructure.Tests` | `dotnet run --project tests/yay_see_sharp.infrastructure.Tests` | 144 | 144 | 0 | 0 |
| `tests/yay_see_sharp.application.Tests` | `dotnet run --project tests/yay_see_sharp.application.Tests` | 89 | 89 | 0 | 0 |
| `tests/yay_see_sharp.integration.Tests` (manuálny beh, nie CI na `pull_request`) | `dotnet run --project tests/yay_see_sharp.integration.Tests` | 14 | 11 | 0 | 3 (gated na Arch/CachyOS host) |
| `tests/yay_see_sharp.e2e.Tests` (Avalonia Headless) | `dotnet run --project tests/yay_see_sharp.e2e.Tests` | 8 | 8 | 0 | 0 |

3 skipnuté testy v `integration.Tests` sú deštruktívne (reálne inštalujú/odinštalujú balík `hello` cez `yay`) a gated cez `YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1` — bežia iba na Arch/CachyOS hoste s `yay` na `PATH` (self-hosted CachyOS CI runner, `push`/`workflow_dispatch` only — pozri NEW-01 nižšie). Na tomto vývojovom stroji sa preto automaticky preskakujú; to je očakávané správanie, nie zlyhanie.

## Zmeny z tejto session (code review + UI/UX findings)

Táto session prešla kompletný zoznam review findings (`docs/code-review-findings.md`) aj používateľské UI/UX nálezy. Zhrnutie zmien oproti stavu vyššie zdokumentovanému k 2026-08-05:

- **FINDING-04 + NEW-05** (AUR/Official/Foreign klasifikácia): `PackageSource` má nový `Foreign` člen. `pacman -Qm` sa už nemapuje priamo na `Aur` — mapuje sa na `Foreign`, kým sa nepotvrdí cez bulk `yay -Si` dotaz (yay transparentne zlučuje sync-db + AUR RPC info). `AurCount` v štatistikách počíta len potvrdené AUR balíky.
- **NEW-04** (scheduler DST): `UpdateScheduleCalculator` teraz berie explicitný `TimeZoneInfo` a používa `TimeZoneInfo.ConvertTimeToUtc`/transition rules namiesto zachovávania "dnešného" UTC offsetu. Spring-forward (neexistujúci čas) sa posúva na najbližší platný okamih; fall-back (duplicated hour) sa rieši ako standard-time (druhý) výskyt. `IClock` má nový `LocalTimeZone` člen.
- **FINDING-06 + NEW-02 + NEW-03** (single-instance IPC): `TryAcquire()` je teraz transakčný — listener failure uvoľní lock a vráti `false` s `LastFailureReason`. `Dispose()` maže activation socket PRED uvoľnením lock streamu (nie po). Runtime adresár sa overuje na owner-only permissions pred použitím.
- **FINDING-08 + NEW-07**: `IEngineDetector` presunuté z `infrastructure.Platform` do `domain.Abstractions`. `ArchitectureTests` rozšírený — zakazuje akýkoľvek typ (vrátane interfaces) z `infrastructure` namespace na ViewModel fields/constructor parametroch.
- **FINDING-09**: `BuildDirectory` browse UI (tlačidlo + folder browser modal) odstránené zo `SettingsView`/`SettingsViewModel`. Model field `BuildDirectory` a `IBuildDirectoryPolicy` runtime wiring zostávajú nezmenené (future feature).
- **NEW-06**: nové testy `YayBackendInstallerTests` (CachyOS pacman cesta, plain-Arch AUR bootstrap cesta, success/failure/cancellation, presné `CommandRequest`/`WorkingDirectory`, temp dir cleanup). `BackendInstallPromptViewModel.ConfirmAsync` má teraz `try/catch` (exception → terminálny Failed stav, nie zamrznutý modal) a operácia je opakovateľná po zlyhaní. Cancel tlačidlo pridané do `BackendInstallPromptView`.
- **FINDING-12**: `ProcessSudoInvoker.RefreshWithPasswordAsync` má teraz 30s timeout (predtým iba `ValidateTimestampAsync` mal timeout).
- **NEW-01**: `.github/workflows/ci.yml` rozdelený na `pr-checks` (GitHub-hosted `ubuntu-24.04`, len `pull_request`, build+unit+E2E, žiadne destructive Arch testy) a `full-suite` (self-hosted CachyOS, len `push`/`workflow_dispatch`).
- **NEW-08**: `Directory.Build.props` centralizuje `OutputPath=$(Configuration)`-scoped; každý `IntermediateOutputPath` má tiež `$(Configuration)` segment.
- **UI-01 až UI-23**: toast notification systém (`ToastService`, `IToastService`-ekvivalent cez existujúci `INotificationService`, `CompositeNotificationService`), Settings presunuté na spodok sidebaru, live search s debounce + odporúčané balíky pri prázdnom query, klikateľné riadky (Dashboard update list, Search results) navigujúce do detailu, Refresh tlačidlá odstránené v prospech auto-refresh po operáciách, PKGBUILD veľké Close tlačidlo, dynamický popis update schedule, tray ikona viditeľná od štartu + minimize-to-tray, `.desktop` súbor (`packaging/yay-see-sharp.desktop`), fonty zdvojnásobené naprieč aplikáciou. Podrobnosti pri jednotlivých sekciách nižšie.

- **Vulnerability scan:** `dotnet list yay_see_sharp.slnx package --vulnerable --include-transitive` — žiadne známe zraniteľné balíky (pozri sekciu "Verifikácia" nižšie pre presný výstup pri poslednom behu).
- **Manuálna GUI verifikácia:** **nebola vykonaná.** Sandbox nemá X11/Wayland displej, `notify-send`/D-Bus session ani reálny Arch/CachyOS host. Všetko nižšie označené "Hotové" je hotové a **testované na úrovni kódu** (unit/integration/headless E2E testy), nie vizuálne overené v bežiacej aplikácii na reálnom hardvéri. Kde je rozdiel medzi "kód existuje a je testovaný" a "overené na reálnom Arch hoste" významný, je to uvedené explicitne pri danej položke.

## Architektúra — 3 source assembly

| Projekt | Obsah | Poznámka |
| --- | --- | --- |
| `source/yay_see_sharp.domain` | Modely, rozhrania (`IPackageBackend`, `IPrivilegeService`, `INotificationService`, `IBuildDirectoryPolicy`, `IBackendInstaller`, ...), `NullNotificationService` | Žiadna závislosť na Avalonic ani na konkrétnom OS I/O. `NullNotificationService` sem bol presunutý z `infrastructure` (je to čistý no-op bez I/O, takže tu patrí — pozri Finding #08 nižšie). |
| `source/yay_see_sharp.infrastructure` | `YayPackageBackend`, `DemoPackageBackend`, `PacmanQueryService`, HTTP/filesystem služby, sudo elevácia, scheduler, notifikácie, `EngineDetector`, `DistributionDetector` | Žiadna závislosť na Avalonia. |
| `source/yay_see_sharp.application` | Avalonia Views, ViewModely, `AppBootstrapper` (jediný production composition root) | Referencuje domain + infrastructure. |

## Doména a backendy

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| `IPackageBackend` kontrakt | Hotové | `PackageBackendContractTestsBase` → `DemoBackendContractTests`, `YayBackendContractTests` | Zdieľané kontraktné testy overujú identické správanie Demo a Yay backendu. |
| **Backend availability detection (FINDING-01)** | **Hotové** | `PackageBackendFactoryTests`, `EngineDetectorTests`, `PackageBackendTests` (distribution detector) | `PackageBackendFactory` teraz overuje `yay` dostupnosť cez `IEngineDetector` (jediný zdroj pravdy) predtým, než označí backend za `Real`. Arch/CachyOS bez `yay` na `PATH` → `BackendMode.Unavailable` (bezpečný Demo-backed fallback + explicitný warning v UI), nie tiché `Real` mode. `EngineDetector` navyše overuje aj exec bit (súbor existujúci, ale nespustiteľný, sa už nepovažuje za "yay na PATH"). |
| **Missing-backend install flow (FINDING-10)** | **Hotové (kód + unit testy), reálna inštalácia yay na Arch nebola manuálne overená** | `YayBackendInstaller` (nový), `BackendInstallPromptViewModel` + View | Pri `BackendMode.Unavailable` sa pri štarte automaticky ponúkne dialog s presným command preview (`sudo pacman -S --needed --noconfirm yay` na CachyOS, `git clone` + `makepkg -si` na plain Arch) a Confirm/Close tlačidlami. Elevácia cez existujúci `IPrivilegeService` flow, žiadny shell string. **Vedomé zjednodušenie:** po úspešnej inštalácii sa backend v bežiacej session NEPREPÍNA naživo (child ViewModely už boli postavené nad pôvodným Demo-backed backendom pri štarte) — UI zobrazí hint, že treba appku reštartovať. Plný live-swap by vyžadoval širšiu zmenu architektúry (mutable backend provider naprieč Dashboard/Search/Installed), čo presahuje rozsah tohto findingu. |
| `YayPackageBackend` | Hotové | `PackageBackendTests` (~45 testov) | Search/details/statistics/updates/install/uninstall/update, `IAsyncEnumerable<PackageOperationProgress>` streaming pre operácie. |
| **Real backend statistics (FINDING-02)** | **Hotové** | `PackageBackendTests` (explicit/dependency/AUR/orphan/updates/size + "unknown vs. zero" testy) | Nový `PacmanQueryService` centralizuje `pacman -Qe/-Qd/-Qm/-Qdt/-Qu/-Qi` query a ich parsing. Každé pole v `PackageStatistics` (okrem `InstalledCount`) je teraz `int?`/`long?` — zlyhanie jednej query vráti `null` (Unknown), nie falošnú nulu. `pacman`-ov "exit 1 s prázdnym výstupom" (0 zhôd) sa správne interpretuje ako `0`, nie ako zlyhanie. |
| **GetDetailsAsync pre nenainštalované balíky (FINDING-03)** | **Hotové** | `PackageBackendTests` (installed/-Si/-Sia fallback reťazec) | `-Qi` → `-Si` (official) → `-Sia` (AUR) fallback reťazec. `YayOutputParser.ParseInfo` má voliteľný `sourceHint`, použitý keď `-Sia` output nemá "Repository" pole. |
| **AUR/Official klasifikácia (FINDING-04)** | **Hotové** | `PackageBackendTests` (parser classification testy) | `ParseInstalled`/`ParseUpdates` teraz prijímajú voliteľný `IReadOnlySet<string> foreignPackageNames` (z `pacman -Qm`) a klasifikujú podľa neho — nie viac vždy `Official`. |
| `DemoPackageBackend` | Hotové | `PackageBackendTests` + `DemoBackendContractTests` | Graceful `Failed`/`Cancelled` progress namiesto výnimiek, `UpdateAsync` all-or-nothing pri neznámom názve. |
| **BuildDirectory runtime consumer (FINDING-09)** | **Hotové** | `PackageBackendTests` (`--builddir` wiring, `~` expansion, missing/not-writable directory) | `YayPackageBackend` teraz berie voliteľný `IBuildDirectoryPolicy` (implementuje ho `SettingsViewModel`) a pri Install/Update pridáva `--builddir {expandedPath}` do `ArgumentList` (nikdy shell string). `~` expanduje na `Environment.SpecialFolder.UserProfile`. Ak nakonfigurovaný priečinok neexistuje alebo nie je writable → operácia zlyhá s presnou chybovou správou v UI namiesto pádu alebo tichého ignorovania nastavenia. |
| **Argument/flag injection hardening (FINDING-11)** | **Hotové** | `PackageBackendTests` (leading-dash package name, invalid character, `--` separator v search) | `PackageArgumentValidator` validuje package names proti Arch naming rules (`^[a-zA-Z0-9@._+-]+$`, nesmie začínať `-`) pred akýmkoľvek Install/Uninstall/Update volaním. Search query teraz vždy posiela `--` separator pred pozičný argument (`yay -Ss -- <query>`). `ArgumentList` (nie shell string) bolo zachované všade. |
| `PackageBackendFactory` | **Čiastočné** | `PackageBackendFactoryTests` | **Chýba:** `ParuPackageBackend` (žiadny reálny `paru` backend zatiaľ neexistuje) — pozri "Engine picker" nižšie. |

## UI / theming

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| App shell + Dark/Light theme systém | Hotové (build+XAML kompilácia + headless E2E overené) | `AppShellE2ETests`, `SettingsE2ETests` | `Sidebar_renders_one_realized_list_box_item_per_navigation_entry` je regresný test pre reálny bug (chýbajúci `Template` v `NavListBox` ControlTheme spôsobil, že sidebar sa vôbec nevykresľoval) objavený pri generovaní screenshotov cez Avalonia Headless. |
| Obrazovky (Dashboard, Search, Installed, Package details, Settings, Password prompt, Tray, Folder browser, **Backend install prompt — nová, FINDING-10**) | Hotové (build+XAML kompilácia + headless E2E) | ViewModel testy + E2E testy | |
| PKGBUILD viewer (in-app modal) | Hotové | `PkgbuildViewModelTests` (vrátane cancellation-on-close), `PkgbuildFetchIntegrationTests` | Pozri FINDING-13 nižšie pre cancellation detaily. |
| Lokalizácia EN/SK, live prepínanie jazyka | Hotové | `LocalizationServiceTests` + testy naprieč ViewModelmi, `SettingsE2ETests` (regresný test na pôvodný ComboBox-empties-on-language-switch bug) | |

## Composition root (FINDING-08)

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| ViewModely neinštanciujú Infrastructure priamo | **Hotové** | `ArchitectureTests.No_ViewModel_field_or_constructor_parameter_is_typed_as_a_concrete_infrastructure_class` (reflection-based, vynucuje pravidlo pri buildovaní testov, nie len disciplínou) | `SettingsViewModel`, `FolderBrowserViewModel`, `PkgbuildViewModel`, `SearchViewModel`, `InstalledPackagesViewModel`, `PackageDetailsViewModel` teraz prijímajú `IEngineDetector`/`IFolderBrowserService`/`IPkgbuildService` ako povinné (nie `?? new Concrete()`) constructor parametre. Všetky reálne inštancie sa vytvárajú v `AppBootstrapper`. `DesignMainWindowViewModel` je explicitný, samostatný design-time factory (jediné miesto mimo `AppBootstrapper`, kde sa smie vytvoriť konkrétna Infrastructure trieda priamo — nikdy nebeží v produkcii). `NullNotificationService` bol presunutý do `domain` projektu (je to čistý no-op bez I/O, takže odkaz naň z ViewModelu nie je DIP porušenie). |

## Single instance, lock file, scheduler (FINDING-05, #06, #07)

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| **Scheduler timezone (FINDING-05)** | **Hotové** | `UpdateSchedulerTests` (UTC, CET zimný `+1`, CEST letný `+2`, zmena schedule za behu, disable/re-enable) | `IClock.LocalNow` (nový člen) — scheduler porovnáva `TimeOnly` proti lokálnemu času, nie UTC. `NextScheduledRun` sa invaliduje pri zmene `UpdateScheduleTime` alebo enable/disable toggle. |
| **Single-instance IPC activation (FINDING-06)** | **Hotové** | `FileLockSingleInstanceServiceTests` (activation request received, no-listener-returns-false, listener stops cleanly) | `ISingleInstanceService.TryActivateExisting()` + `ActivationRequested` event. Implementácia cez Unix domain socket (`$XDG_RUNTIME_DIR/yay_see_sharp/activate.sock`). Druhá inštancia po zlyhaní `TryAcquire()` pošle activation message a skončí; prvá inštancia reaguje cez `Dispatcher.UIThread.Post` (window Show/WindowState/Activate). |
| **Lock file path (FINDING-07)** | **Hotové** | `FileLockSingleInstanceServiceTests` (dispose nemaže lock file, adresár má `0700` na Linuxe) | Presunuté z predvídateľného `/tmp/yay_see_sharp.lock` na `$XDG_RUNTIME_DIR/yay_see_sharp/instance.lock` (per-user, nie world-writable `/tmp`). |

## Privilege elevation, notifikácie

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| `IPrivilegeService` / `SudoPrivilegeService` | **Hotové (kód + unit testy), interaktívny sudo prompt manuálne neoverený** | `SudoPrivilegeServiceTests`, `ProcessSudoInvokerTests` (nový, reálny `sudo -n -v` binary), elevation scenáre v `PackageBackendTests` | `sudo -S -v` cez stdin pipe, heslo nikdy v argv/logoch. |
| **Sudo process cancellation (FINDING-12)** | **Hotové** | `ProcessSudoInvokerTests` (already-cancelled token → `false` bez výnimky, stabilné opakované volania) | `ProcessSudoInvoker` teraz pri cancellation explicitne `Kill(entireProcessTree: true)`-uje sudo proces, zavrie stdin, await-ne exit na neasercovateľnom tokene (cleanup nemôže znova hodiť `OperationCanceledException`) a vráti `false` namiesto propagovania výnimky — konzistentné s existujúcim `SystemCommandRunner` vzorom. `ValidateTimestampAsync` má nový 10s timeout (linked CTS) — `sudo -n -v` sa nikdy nepýta na heslo, takže hang znamená zaseknutý PAM modul, nie legitímne čakanie. |
| `IUpdateScheduler` | Hotové | `UpdateSchedulerTests` | Pozri FINDING-05 vyššie pre timezone fix. |
| `INotificationService` — desktop notifikácie | **Hotové (kód + unit testy), manuálne neoverené** | `NotifySendNotificationServiceTests`, `SettingsAwareNotificationServiceTests` | Reálne `notify-send` odoslanie nebolo manuálne overené — sandbox nemá notification daemon. |
| **PKGBUILD fetch cancellation (FINDING-13)** | **Hotové** | `PkgbuildViewModelTests` (close-during-fetch → žiadny error, close-before-load) | `PkgbuildViewModel` teraz vlastní `CancellationTokenSource`; `CloseCommand` ho zruší pred dokončením close tasku. `LoadAsync` rozlišuje "naša cancellation" (očakávané, žiadny `ErrorMessage`) od skutočného zlyhania/timeoutu (zobrazí sa ako error). `SharedHttpClient.Instance.Timeout` je teraz explicitne `15s` (namiesto BCL default 100s). |

## Engine picker (Option B — dočasné obmedzenie na yay)

| Oblasť | Stav | Poznámka |
| --- | --- | --- |
| Voľba `yay`/`paru` v Settings | **Čiastočné — dočasne obmedzené len na `yay`** | `paru` engine nikdy nebol implementovaný ako backend (žiadny `ParuPackageBackend`); `design_handoff/README.md` toto explicitne označuje ako future feature (DOC-03). |

## Package verzie — `Avalonia.ReactiveUI` (FINDING-14)

Overené znova cez NuGet API (`avalonia.reactiveui/index.json`) — **žiadny 12.x release stále neexistuje**, `11.3.9` zostáva najnovší. Rozhodnutie ponechať 11.3.9 popri 12.1.1 core je zdokumentované ako ADR priamo v `Directory.Packages.props` s regresným pokrytím cez `tests/yay_see_sharp.e2e.Tests` (headless beh reálnej appky s týmto presným package graphom, vrátane `ReactiveCommand` exekúcie naprieč navigáciou/search/theme/language). `dotnet list package --include-transitive` neobsahuje žiadny iný 11.x Avalonia core balík (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`, `Avalonia.Skia`, `Avalonia.X11`, ... sú všetky `12.1.1`).

## Vedomé hranice rozsahu / známe medzery

1. **`paru` engine** — nikdy neimplementovaný ako backend; UI ho neponúka.
2. **Manuálna GUI/vizuálna verifikácia** — nebola a nemôže byť vykonaná v tomto headless sandboxe. Headless E2E testy (Avalonia.Headless) čiastočne zmierňujú toto riziko — skutočne vykresľujú compiled Views cez reálny Skia renderer a boli použité aj na zachytenie reálnych screenshotov (`docs/screenshots/`) — ale nenahrádzajú overenie na reálnom X11/Wayland desktope, interaktívny sudo prompt, tray ikonu ani skutočné D-Bus notifikácie.
3. **Reálny Arch/CachyOS host** — 3 integračné testy (real install/uninstall) a celý "Real mode" backend (statistics, AUR classification, backend install flow) sú testované proti mockovanému/fake `ICommandRunner`, nie proti skutočnému `yay`/`pacman` behu. Kód je navrhnutý podľa dokumentovaného správania `pacman`/`yay`, ale nebol spustený na reálnom Arch systéme v rámci tejto session.
4. **Obsah z backendu za behu** (názvy/popisy balíkov, technické `Exception.Message` chybové hlásenia) sa neprekladá.
5. **`AvaloniaTrayService`** je funkčne lokalizovaný, no bez automatizovaného testu (GUI/OS-only funkcionalita).
6. **Backend install flow (FINDING-10) nerobí live swap** — po úspešnej inštalácii `yay` treba appku reštartovať, aby sa reálne prepla na Real mode (pozri FINDING-10 vyššie).
7. **3 integračné testy sú trvalo gated** na reálny Arch/CachyOS host s `yay` (`YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1`).
