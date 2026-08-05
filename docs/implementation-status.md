# yay_see_sharp — Implementation Status

> Tento dokument popisuje **skutočný stav behu kódu** ku dňu poslednej aktualizácie, nie plán. Kde je funkcia len čiastočná, je to explicitne uvedené aj s tým, čo chýba. Automatizovaná verifikácia (build + testy) je striktne oddelená od manuálnej GUI verifikácie — druhá menovaná v tomto sandboxe (bez displeja/D-Bus) nebola a nemôže byť vykonaná.

## Posledný build/test stav (automatizovaná verifikácia)

- **Build:** `dotnet build --configuration Debug` — 0 warnings, 0 errors, celé riešenie (`yay_see_sharp.application`, `yay_see_sharp.unittests`, `yay_see_sharp.integrationtests`).
- **Unit testy** (`tests/yay_see_sharp.unittests`, `dotnet run --project ...`): **162/162 passed, 0 skipped.**
- **Integračné testy** (`tests/yay_see_sharp.integrationtests`, spúšťané manuálne, nie v CI): **14 total — 11 passed, 3 skipped.** Skipnuté 3 testy sú explicitne gated na reálny Arch/CachyOS host s nainštalovaným `yay` cez `YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1` (na tomto Ubuntu vývojovom stroji sa automaticky preskakujú — to je očakávané správanie, nie zlyhanie).
- **Manuálna GUI verifikácia:** **nebola vykonaná.** Sandbox nemá X11/Wayland displej ani `notify-send`/D-Bus session, takže vizuálny vzhľad, sudo prompt dialóg, tray ikonu a skutočné desktop notifikácie nie je možné v tomto prostredí odskúšať očami. Všetko nižšie označené "Hotové" je hotové a **testované na úrovni kódu** (unit/integration testy), nie vizuálne overené v bežiacej aplikácii.

## Doména a backendy

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| `Domain/Models` (`PackageSummary`, `PackageDetails`, `PackageStatistics`, `UpdateInfo`, `PackageOperationProgress`, `BackendInfo`, ...) | Hotové | — | Nezmenené od predchádzajúcich iterácií. |
| `IPackageBackend` kontrakt | Hotové | Pozri kontraktné testy nižšie | Search/details/statistics/updates/install/uninstall/update s `IAsyncEnumerable<PackageOperationProgress>` streamovaním pre operácie. |
| `YayPackageBackend` | Hotové | `PackageBackendTests` (~20 testov vrátane elevation scenárov) | Všetky metódy IPackageBackend implementované cez `yay` CLI + `ICommandRunner`. Install/Uninstall/Update volajú `TryElevateAsync` pred spustením príkazu (pozri sekciu Privilege elevation nižšie). |
| `YayOutputParser` | Hotové | `PackageBackendTests` (parser: search, info, updates, installed) | Parsuje textový výstup `yay -Ss/-Qi/-Qu/-Q`. |
| `DemoPackageBackend` | **Hotové (opravený kontrakt — Finding #10)** | `PackageBackendTests` + `DemoBackendContractTests` | Pôvodne sa správal odlišne od `YayPackageBackend`: hádzal `InvalidOperationException` pri neznámom balíku namiesto `Failed` progress eventu, nezvládal zrušenie (unhandled `OperationCanceledException`), nemal spôsob simulovať zlyhanie platného balíka a `UpdateAsync` ticho ignoroval neznáme názvy namiesto zlyhania celej operácie. Všetky štyri opravené: pridaný `simulatedFailures` konštruktorový parameter, `TryDelayAsync` helper pre graceful cancellation, `Failed`/`Cancelled` progress namiesto výnimiek, `UpdateAsync` teraz all-or-nothing pri neznámom názve (zhoduje sa so správaním pacman/yay). |
| `PackageBackendFactory` | **Čiastočné** | `PackageBackendFactoryTests` | Arch/CachyOS → `YayPackageBackend`, iné distro → `DemoPackageBackend`. **Chýba:** vetvenie na `ParuPackageBackend` — `Settings.Engine` je momentálne obmedzený len na hodnotu `Yay` (pozri "Engine picker" nižšie), `paru` vetva je označená `// TODO: paru support` v `PackageBackendFactory.cs:34` a nie je implementovaná. |
| **Kontraktné testy Demo vs. Yay backend (Finding #10)** | **Hotové** | `PackageBackendContractTestsBase` (abstraktná, `[InheritsTests]`) → `DemoBackendContractTests`, `YayBackendContractTests` = 12 zdieľaných testov × 2 backendy = 24 testov | Overuje identické správanie oboch implementácií `IPackageBackend` pre: install (úspech/zlyhanie/neplatný názov/cancel), uninstall (`removeOrphans` true aj false, cancel), update (all/selected/neplatný názov/cancel), a zachovanie `Output` textu vo finálnom progress evente. `YayPackageBackend` je v týchto testoch napojený na nový `FakeYayCommandRunner` (test double, **nie** mock so striktnými interakčnými očakávaniami) — stavový simulátor, ktorý rozpoznáva presné argumenty, aké produkuje Install/Uninstall/Update (`--needed --noconfirm -S`, `--noconfirm -Rns`/`-Rn`, `-Syu --noconfirm`, `-S --noconfirm --needed <pkgs>`) a udržiava vlastný set nainštalovaných balíkov. Skutočný `yay` binárny súbor sa v týchto testoch nepoužíva. |

## UI / theming

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| App shell (140px sidebar, navigácia) + Dark/Light theme systém | Hotové (build+XAML kompilácia overené) | — | `Themes/Colors.axaml`, `Themes/Controls.axaml`, `ThemeVariant` naviazaný na `Settings.Theme` reaktívne v `App.axaml.cs`. Vizuálne neoverené (žiadny displej v sandboxe). |
| 9 obrazoviek (Loading, Dashboard, Search, Installed, Package details, Settings, Password prompt, Tray, Folder browser) | Hotové (build+XAML kompilácia overené) | ViewModel testy pre každú (pozri nižšie) | Vektorové ikony (`Views/Icons/*`) namiesto rastrových assetov. |
| PKGBUILD viewer (in-app modal) | Hotové | `PkgbuildViewModelTests`, `PkgbuildFetchIntegrationTests` | Fetchuje raw text z `https://aur.archlinux.org/cgit/aur.git/plain/PKGBUILD?h={pkgname}` cez `IPkgbuildService` → `PkgbuildService` (real `HttpClient`), zobrazí v scrollovateľnom monospace modale. Chyby (network/404) sa zobrazia ako `ErrorMessage` v modale, nie pád aplikácie. |
| Lokalizácia EN/SK, live prepínanie jazyka | Hotové | `LocalizationServiceTests` + testy naprieč všetkými ViewModelmi (`LocalizedViewModelBase`) | Vrátane `Dashboard.Relative.*` kľúčov pre relatívny čas (bod nižšie). |
| `DashboardViewModel.FormatRelative` (Finding #11) | **Hotové** | `DashboardViewModelTests` (4 testy: en/sk, singulár/plurál, "moments ago") | Pôvodne natvrdo anglický text (`"X minutes ago"`), teraz cez `Localization.GetString` s singulárnym/plurálnym kľúčom (`FormatUnit` helper). |

## Filesystem/HTTP I/O abstrakcia (Finding #8)

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| `IFolderBrowserService` / `FolderBrowserService` | Hotové | `FolderBrowserViewModelTests` (mockovaná služba), `FolderBrowserFilesystemIntegrationTests` (reálny `/tmp`) | `FolderBrowserViewModel` už priamo nevolá `Directory.*` — všetka I/O je za rozhraním. |
| `IPkgbuildService` / `PkgbuildService` | Hotové | `PkgbuildViewModelTests` (mockovaná služba), `PkgbuildFetchIntegrationTests` (reálna AUR sieť) | `PkgbuildViewModel` už priamo nevolá `HttpClient` — nahradené injektovaným rozhraním. |

## Privilege elevation, scheduler, notifikácie (Findings #5, #6, #7)

| Oblasť | Stav | Testy | Poznámka |
| --- | --- | --- | --- |
| `IPrivilegeService` / `SudoPrivilegeService` — reálna sudo elevácia | **Hotové (kód + unit testy), manuálne neoverené** | `SudoPrivilegeServiceTests`, 4 elevation scenáre v `PackageBackendTests` (granted/cancelled/failed × install/uninstall/update) | `sudo -S -v` cez stdin pipe (`ProcessSudoInvoker`), heslo sa nikdy nezapisuje do `CommandRequest.Arguments`, nikdy sa nelogguje ani nepersistuje. `PasswordPrompt` je neskoro naviazaný delegate (rieši cyklickú závislosť konštrukcie: backend potrebuje privilege service skôr, než existuje `MainWindowViewModel` s reálnym UI promptom). **Skutočný interaktívny sudo prompt v behovej aplikácii nebol manuálne odskúšaný** — sandbox nemá TTY/GUI session na to. |
| `IUpdateScheduler` / `UpdateScheduler` — automatický background update check | Hotové | `UpdateSchedulerTests` (s `FakeClock`, `FakeSettings`) | Polling loop rešpektuje `Settings.AutoUpdateCheckEnabled` na každý tick (zapnutie/vypnutie počas behu sa prejaví okamžite, bez reštartu). Pri vývoji objavený a opravený reálny bug: `NextScheduledRun` sa pôvodne prepočítaval z aktuálneho `now` pri každom ticku, čím `UpdateScheduleCalculator.GetNextRun` (ktorý vždy vracia čas `> now`) zaručene nikdy nesplnil podmienku `now >= next` — scheduler by nikdy nespustil beh. Opravené cachovaním `NextScheduledRun` a prepočtom až po skutočnom spustení. |
| `INotificationService` — desktop notifikácie (`notify-send`) | **Hotové (kód + unit testy), manuálne neoverené** | `NotifySendNotificationServiceTests`, `SettingsAwareNotificationServiceTests` | `NotifySendNotificationService` spúšťa `notify-send` procesom; `SettingsAwareNotificationService` obalí a rešpektuje `Settings.NotificationsEnabled`; `NullNotificationService` ako bezpečný default pre testy/prípady bez konfigurácie. **Reálne odoslanie notifikácie cez `notify-send` nebolo manuálne overené** — sandbox nemá notification daemon/D-Bus session. |
| Fire-and-forget async cleanup (Finding #9) | Hotové | Pokryté existujúcimi ViewModel testami (žiadna zmena správania, len bezpečnostná záruka) | Pomenovaný `Task.FireAndForget()` extension helper (`AsyncExtensions.cs`) nahradil 7 miest s "naked" fire-and-forget volaním (riziko unobserved task exceptions); `Install`/`Uninstall`/`RunUpdate` operácie majú teraz try/catch/finally zaručujúce, že `Operation`/`UpdateOperation` sa vždy vyčistí. |

## Engine picker (Finding #4 — Option B)

| Oblasť | Stav | Poznámka |
| --- | --- | --- |
| Voľba `yay`/`paru` v Settings | **Čiastočné — dočasne obmedzené len na `yay`** | `SettingsViewModel.EngineOptions` obsahuje len `PackageManagerEngine.Yay`; konštruktor navyše "clampne" akékoľvek staré uložené `Paru` nastavenie späť na `Yay`, aby UI a perzistovaný stav nikdy nedivergovali. Toto je vedomé dočasné riešenie (Option B z code review) — `paru` engine reálne nikdy nebol implementovaný (žiadny `ParuPackageBackend`), takže predchádzajúci picker bol UI bez funkčného efektu. Skutočná podpora `paru` zostáva mimo rozsahu, kým nevznikne `ParuPackageBackend`. |

## Package verzie — poznámka k `Avalonia.ReactiveUI`

Riadené cez `Directory.Packages.props` (CPM). Avalonia core (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`) je na `12.1.1`. `Avalonia.ReactiveUI` zostáva na `11.3.9`, pretože **k dátumu poslednej kontroly cez NuGet API nemá žiadny 12.x release** — `11.3.9` je jeho najnovšia dostupná verzia. Toto je akceptovaný kompromis, nie prehliadnutá chyba: `Avalonia.ReactiveUI` je tenký shim, ktorý len registruje `AppBuilder.UseReactiveUI()` scheduler a nezávisí od interných rendering/control API Avalonia, takže je pri prechode 11→12 hranici nízkorizikový. Pre porovnanie, `Avalonia.Diagnostics` (F12 dev-tools overlay) má rovnaký problém, ale **žiadnu bezpečnú fallback cestu** (viaže sa priamo na interné API), preto bol namiesto ponechania v nesúladnej verzii úplne odstránený z projektu.

## Vedomé hranice rozsahu / známe medzery

1. **`paru` engine** — nikdy neimplementovaný ako backend; UI ho momentálne ani neponúka (pozri "Engine picker" vyššie).
2. **Manuálna GUI/vizuálna verifikácia** — nebola a nemôže byť vykonaná v tomto headless sandboxe (žiadny displej, žiadny D-Bus/notification daemon, žiadna interaktívna TTY session pre sudo). Všetko postavené na predpoklade, že Avalonia XAML kompilácia + unit testy ViewModelov zachytia regresie na úrovni logiky, nie vzhľadu.
3. **Obsah z backendu za behu** (názvy/popisy balíkov, technické `Exception.Message` chybové hlásenia) sa neprekladá — len statický UI text má EN/SK preklad; dynamické chyby majú aspoň lokalizovaný prefix (`Generic.ErrorPrefix`).
4. **`AvaloniaTrayService`** je funkčne lokalizovaný, no bez automatizovaného testu (GUI/OS-only funkcionalita).
5. **3 integračné testy sú trvalo gated** na reálny Arch/CachyOS host s `yay` (`YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1`) — inštalujú/odinštalúvajú skutočný balík `hello`, preto sú zámerne deštruktívne a nikdy sa nespúšťajú automaticky.

## Testy — celkový prehľad

| Projekt | Total | Passed | Failed | Skipped |
| --- | --- | --- | --- | --- |
| `tests/yay_see_sharp.unittests` | 162 | 162 | 0 | 0 |
| `tests/yay_see_sharp.integrationtests` (manuálny beh) | 14 | 11 | 0 | 3 (gated na Arch host) |
