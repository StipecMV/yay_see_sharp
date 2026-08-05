# yay_see_sharp — Self Review

**Dátum:** 2026-08-04
**Rozsah:** `source/yay_see_sharp.application/**`, `tests/**`, `docs/architecture.md`, `docs/product-requirements.md`, `docs/implementation-status.md`, `design_handoff/README.md`.
**Metóda:** manuálne čítanie zdrojového kódu (nie statická analýza), krížová kontrola voči dokumentácii a voči predchádzajúcemu `docs/code-review-findings.md`.

## Executive summary

Projekt má solídny základ: vrstvenie zodpovedá `architecture.md` (Views → ViewModels → Domain abstrakcie → Infrastructure), žiadny proces sa nespúšťa cez shell string interpoláciu (všade `ArgumentList`), a všetkých 12 findingov z predchádzajúceho `docs/code-review-findings.md` je v kóde skutočne vyriešených — vrátane `removeOrphans`/`RemoveOrphansByDefault` prepojenia, sudo privilege flow, schedulera, notifikácií a I/O abstrakcií. Testovacie pokrytie (162 unit testov, kontraktné testy Demo vs. Yay) je nad štandard pre MVP tejto veľkosti. Bezpečnostne najcitlivejšia časť — sudo heslo — sa nikdy nedostane do argument listu ani logu.

Zvyšné problémy sú menšieho rozsahu než pôvodné findingy: (1) `MainWindowViewModel` sa správa čiastočne ako composition root a priamo inštancuje konkrétne Infrastructure triedy namiesto DI, čo je DIP/SRP odchýlka; (2) `PkgbuildService` interpoluje názov balíka do URL bez encodovania; (3) žiadny fyzický (assembly-level) hranica medzi Domain/ViewModels/Infrastructure — dnešný súlad s architektúrou stojí na disciplíne, nie na kompilátore; (4) opakovaný `IsBusy`/`try-catch-finally` a operation-lifecycle kód naprieč ViewModelmi (DRY). Žiadny nález nie je CRITICAL.

---

## 1. Súlad s dokumentáciou a architektúrou

| # | Zistenie | Závažnosť | Lokalizácia |
|---|---|---|---|
| 1.1 | Vrstvenie zodpovedá `architecture.md`: Views nikde nevolajú `yay`/`pacman`/`apt`/shell (overené grepom cez `Views/`), ViewModely nerobia priamy filesystem/HTTP I/O (za `IFolderBrowserService`/`IPkgbuildService`), Domain neobsahuje žiadnu referenciu na Avalonia. | **OK** | `source/.../Domain/`, `source/.../Views/` |
| 1.2 | Všetkých 12 findingov z `docs/code-review-findings.md` je opravených v kóde: `-Rns`/`-Rn` vetva (Uninstall.cs:23,40), `RemoveOrphansByDefault` prepojený (`PackageDetailsViewModel.cs:186`), `IPrivilegeService`/`SudoPrivilegeService` reálne zapojený, `IUpdateScheduler` beží, `INotificationService` funguje, I/O za abstrakciami, `Task.FireAndForget()` + try/finally cleanup, kontraktné testy Demo↔Yay, lokalizovaný relatívny čas. | **OK** | viď `docs/implementation-status.md` |
| 1.3 | **Domain/ViewModels/Infrastructure sú jeden a ten istý .csproj/assembly** (`yay_see_sharp.application.csproj`). Architektúra popisuje vrstvy koncepčne, ale nič na úrovni kompilátora nezabráni budúcemu commitu pridať `using Avalonia` do `Domain/` alebo priamy `Process.Start` do `ViewModels/` — dnešný súlad stojí na code review disciplíne, nie na projektovej hranici (žiadny `InternalsVisibleTo`/referenčný zákaz, žiadny architektúrny unit test typu "Domain assembly nesmie referencovať Avalonia"). | **MEDIUM** | `yay_see_sharp.slnx`, chýbajúci `ArchitectureTests` |
| 1.4 | `PackageManagerEngine.Paru` zostáva v UI len ako budúci placeholder (Option B z predch. review) a je to teraz **čestne zdokumentované** na 3 miestach (`SettingsViewModel.cs:289-295`, `PackageBackendFactory.cs:34-37`, `implementation-status.md`). Žiadna funkcionalita sa nesľubuje bez efektu — pôvodný HIGH finding #4 je vyriešený. | **OK** | `SettingsViewModel.cs:289` |
| 1.5 | `Avalonia.ReactiveUI` zostáva na `11.3.9` popri `Avalonia`/`Avalonia.Desktop`/`Avalonia.Themes.Fluent` `12.1.1`. Rozhodnutie je zdokumentované a zdôvodnené (tenký scheduler shim, žiadna závislosť na interných rendering API). Pôvodný review ale explicitne spomínal alternatívny balík `ReactiveUI.Avalonia` cielený na Avalonia 12 — **v tomto reviewe nebolo možné overiť (žiadny network prístup v sandboxe), či tento balík dnes existuje a je zrelý náhradník**; odporúčam si to pred ďalším releasom overiť cez `dotnet list package --include-transitive` / NuGet, aby sa neprijímal cross-major risk dlhšie, než je nutné. | **LOW** (informačné, nie regresia) | `Directory.Packages.props:14` |
| 1.6 | Product requirements: všetkých 12 MVP capabilities z `product-requirements.md` má zodpovedajúci kód a testy (search/filter/install/uninstall/orphans/updates/statistics/operation progress/demo mode/engine detekcia). Nenašiel som nesplnený requirement. | **OK** | — |

---

## 2. MVVM

| # | Zistenie | Závažnosť | Lokalizácia |
|---|---|---|---|
| 2.1 | Views sú deklaratívne, code-behind súbory (11–41 riadkov) len castujú `DataContext` a volajú `ReactiveCommand.Execute(...).Subscribe()` z pointer/tap eventov — žiadna business logika, žiadne priame volanie backendu/služieb z code-behind. | **OK** | `Views/SettingsView.axaml.cs:17-39` |
| 2.2 | Commands sú správne postavené s `CanExecute` observables tam, kde má zmysel: `PackageDetailsViewModel` gate-uje Install/Uninstall podľa stavu balíka (`canInstall`/`canUninstall`, riadky 38-42), `AuthPromptViewModel.AuthenticateCommand` je gatnutý neprázdnym heslom, `OperationViewModel.CancelCommand` je gatnutý `IsRunning`. | **OK** | `PackageDetailsViewModel.cs:37-43` |
| 2.3 | **`MainWindowViewModel` funguje čiastočne ako composition root**, nielen ako ViewModel dashboardu/navigácie: v konštruktore priamo inštancuje `new SettingsViewModel(...)`, `new SettingsAwareNotificationService(new NotifySendNotificationService(new SystemCommandRunner()), Settings)`, `new DashboardViewModel(...)`, `new SearchViewModel(...)`, `new InstalledPackagesViewModel(...)`. To znamená, že ViewModel vrstva referencuje a inštancuje konkrétne `Infrastructure.Notifications`/`Infrastructure.Process` triedy priamo, namiesto injektovania hotových inštancií/abstrakcií zvonku (z `App.axaml.cs`, kde je zvyšok composition rootu — `SudoPrivilegeService`, `PackageBackendFactory`, `SystemCommandRunner` pre backend). Composition-root zodpovednosť je tak rozdelená na dve miesta nekonzistentne. | **MEDIUM** | `MainWindowViewModel.cs:27-32` |
| 2.4 | Viacero ViewModelov má "optional constructor param s konkrétnym Infrastructure defaultom" (poor-man's DI): `FolderBrowserViewModel` → `new FolderBrowserService()` (riadok 28), `PkgbuildViewModel` → `new PkgbuildService()` (riadok 30), `SettingsViewModel` → `new EngineDetector()` (riadok 39), `DashboardViewModel`/`PackageDetailsViewModel` → `NullNotificationService.Instance`. Testovateľnosť je zachovaná (testy injektujú mock), ale ViewModels assembly aj tak kompiluje priamu referenciu na konkrétne Infrastructure typy — súvisí s 2.3 a 1.3. | **LOW** | `FolderBrowserViewModel.cs:28`, `PkgbuildViewModel.cs:30` |
| 2.5 | Žiadny platform-specific kód neuniká do ViewModel vrstvy (potvrdené grepom: `using Avalonia` sa v `ViewModels/` nenachádza). Password handling (`AuthPromptViewModel.Password`) je čistý string bindnutý na `TextBox PasswordChar="•"` bez custom code-behind logiky. | **OK** | `AuthPromptView.axaml:28` |

---

## 3. SOLID

| Princíp | Hodnotenie | Poznámka |
|---|---|---|
| **SRP** | **MEDIUM odchýlka** | `MainWindowViewModel` má dve zodpovednosti (dashboard/nav state + child-VM/infra wiring), pozri 2.3. `SettingsViewModel` je na hrane (settings CRUD + debounce auto-save loop + option-building + folder-browser orchestration), ale je to kohézne k jednej obrazovke — neeskalujem nad LOW. |
| **OCP** | **OK** | `IPackageBackend` umožňuje pridať `AptPackageBackend`/`ParuPackageBackend` bez zmeny ViewModelov (potvrdené existenciou paralelného `DemoPackageBackend` bez zásahu do UI vrstvy). |
| **LSP** | **OK** | `PackageBackendContractTestsBase` (`[InheritsTests]`) beží 12 zdieľaných testov nad `DemoBackendContractTests` aj `YayBackendContractTests` — reálne overuje zameniteľnosť implementácií `IPackageBackend`, nie len zhodu signatúr. Toto je nadštandardná praktika pre MVP. |
| **ISP** | **LOW** | `IPackageBackend` mieša read-only dopyty (Search/GetDetails/GetStatistics/GetUpdates/GetInstalledPackages) a mutácie (Install/Uninstall/Update) v jednom rozhraní (7 metód). Pre dnešný rozsah je to primerané a kohézne k doméne "package backend"; pri raste (napr. read-only reporting klient) by šlo o kandidáta na rozdelenie na `IPackageQueryService`/`IPackageOperationService`. Neeskalujem vyššie, lebo rozhranie je malé a používa sa vždy ako celok. |
| **DIP** | **MEDIUM odchýlka** | Väčšina kódu závisí od abstrakcií (`ISettingsStore`, `ILocalizationService`, `INotificationService`, `IClock`, `IPackageBackend`) — toto je vzorové. Hlavná odchýlka je 2.3/2.4: vysokoúrovňový (ViewModel) kód priamo inštancuje nízkoúrovňové (Infrastructure) triedy namiesto prijatia hotovej inštancie cez konštruktor bez defaultu / DI kontajner. |

---

## 4. KISS a DRY

| # | Zistenie | Závažnosť | Lokalizácia |
|---|---|---|---|
| 4.1 | Vzor `IsBusy = true; ErrorMessage = null; try { ... } catch (Exception ex) { ErrorMessage = ex.Message; } finally { IsBusy = false; }` sa opakuje takmer identicky v `SearchViewModel.SearchAsync`, `InstalledPackagesViewModel.RefreshAsync`, `PackageDetailsViewModel.LoadAsync`, `DashboardViewModel.RefreshAsync`. Kandidát na spoločný `protected Task RunBusyAsync(Func<Task> action)` helper v `ViewModelBase`. | **LOW** (DRY) | `SearchViewModel.cs:123`, `InstalledPackagesViewModel.cs:90`, `PackageDetailsViewModel.cs:133`, `DashboardViewModel.cs:158` |
| 4.2 | Vzor "vytvor `OperationViewModel` → `await foreach` progress → notifikuj outcome → catch → `finally` vyprázdni `Operation`" je takmer doslovne triplikovaný v `PackageDetailsViewModel.InstallAsync`/`UninstallAsync` a `DashboardViewModel.RunUpdateAsync`. Extrakcia do jedného helpera (napr. `PackageOperationRunner.RunAsync(kind, operationFactory, notifyKey, ...)`) by znížila riziko, že budúca zmena (napr. nový notification level) sa opraví len na dvoch z troch miest. | **LOW/MEDIUM** (DRY) | `PackageDetailsViewModel.cs:151-217`, `DashboardViewModel.cs:186-222` |
| 4.3 | Žiadny mŕtvy kód ani nevyužité abstrakcie nájdené — `PackageManagerEngine.Paru` je zámerný forward-compat placeholder (zdokumentovaný, nie zabudnutý), `GlobalSuppressions.cs` má presne jednu odôvodnenú suppression. | **OK** | — |
| 4.4 | Riešenia sú primerane jednoduché pre rozsah MVP — nenašiel som zbytočnú abstrakciu (napr. netreba generický repository/CQRS nad settings, čo by bolo over-engineering pre jeden JSON súbor). `FileSettingsStore`, `UpdateScheduler` (poll namiesto komplexného schedulera) sú KISS-konformné a zdokumentované prečo. | **OK** | `UpdateScheduler.cs:8-11` |

---

## 5. Cybersecurity

Toto je najkritickejšia sekcia — nižšie je to, čo som overil, s dôkazom.

### 5.1 Sudo/privilege — `SudoPrivilegeService`, `ProcessSudoInvoker`

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.1.1 | Heslo sa **nikdy neposiela cez argument list** — `ProcessSudoInvoker.RefreshWithPasswordAsync` píše heslo cez `process.StandardInput.WriteLineAsync(password)` na `sudo -S -v`, argument list obsahuje iba `["-S", "-v"]`. Argument list by bol viditeľný cez `/proc/<pid>/cmdline` pre iných používateľov na systéme — toto riziko je správne eliminované. | **OK** |
| 5.1.2 | Heslo sa nikde neloguje (overené grepom `Log()` volaní v celom projekte — jediné dve existujú v `NotifySendNotificationService` a nesúvisia s heslom) a nikdy sa nepersistuje (`ISettingsStore`/`AppSettings` model neobsahuje pole na heslo). | **OK** |
| 5.1.3 | Žiadny timing-attack vektor na aplikačnej úrovni: samotné porovnanie hesla robí `sudo`/PAM v samostatnom procese, aplikácia iba odovzdá heslo cez stdin a číta exit code — aplikácia neimplementuje vlastné porovnávanie reťazcov, takže nemá čo timing-leaknúť. | **OK** |
| 5.1.4 | `SecureString` sa zámerne nepoužíva — zdokumentované ako súlad s aktuálnym .NET guidance (SecureString je na cross-platforme deprecated/no-op). Heslo je krátkožijúci `string` v `RequestElevationAsync`, referencia sa v `finally` nuluje. **Zvyškové riziko**: `AuthPromptViewModel._password` field (View-side) nie je po `Resolve()` explicitne vynulovaný — inštancia zostáva žiť, kým ju GC nezoberie (`MainWindowViewModel.AuthPrompt = null` len odpojí referenciu z `MainWindowViewModel`, samotný `AuthPromptViewModel` s heslom v poli môže byť v pamäti ešte chvíľu). Vzhľadom na to, že .NET stringy sú beztak needitovateľné (nedá sa reálne "vymazať" obsah), ide o teoretické, nie prakticky zneužiteľné zlepšenie. | **LOW** |
| 5.1.5 | `RequestElevationAsync`/`RefreshIfNeededAsync` majú dobré pokrytie testami (`SudoPrivilegeServiceTests`, 8 scenárov: granted/cancelled/failed/re-prompt/fail-closed-bez-promptu/refresh). Fail-closed správanie keď `PasswordPrompt` nie je zapojený (`SudoPrivilegeService.cs:42-45`) je správny bezpečný default. | **OK** |

### 5.2 Command injection — `CommandRequest`, `SystemCommandRunner`, `ProcessSudoInvoker`

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.2.1 | Všetky spustenia procesov (`SystemCommandRunner`, `ProcessSudoInvoker`, `NotifySendNotificationService`) používajú `ProcessStartInfo.ArgumentList` s `UseShellExecute = false` — **nikde v projekte sa nenašla shell string interpolácia** (`/bin/sh -c "..."` alebo podobné). Toto úplne eliminuje klasický shell command injection (žiadne `;`, `` ` ``, `$()`, `|` nemôžu uniknúť z argumentu do shellu, lebo shell sa vôbec nespúšťa). | **OK** |
| 5.2.2 | **Argument/flag injection do `yay`/`sudo` samotného je teoreticky možný**, keďže vstup (search query, package name) nie je nikde validovaný proti prefixu `-`. Napr. `SearchAsync` posiela `query.Trim()` priamo ako pozičný argument do `yay -Ss <query>` (`YayPackageBackend.cs:71`) — keby používateľ zadal do search boxu reťazec začínajúci pomlčkou (napr. `--upgrade` alebo podobný `pacman`/`yay` flag), `yay`/`pacman`-štýl argument parser by ho mohol interpretovať ako ďalšiu voľbu namiesto vyhľadávacieho výrazu. Rovnaké platí pre `packageName` v `InstallAsync`/`UninstallAsync`/`UpdateAsync` (`YayPackageBackend.Install.cs:38`, `Uninstall.cs:40`), hoci tam reálne `packageName` pochádza zo search results (klikateľná položka), nie z voľného textového poľa. Toto **nie je OS-level command injection** (žiadny nový proces sa nespustí, žiadny shell), ale je to legitímny "argument confusion" vektor, ktorý stojí za doplnenie `--` separátora pred pozičné argumenty (podporované aj `pacman`, aj `yay`), príp. validácie že vstup nezačína `-`. | **MEDIUM** |
| 5.2.3 | `SystemCommandRunner` korektne prepošle `CancellationToken` a pri zrušení robí `process.Kill(entireProcessTree: true)` — zabraňuje osireným child procesom po cancel. | **OK** |

### 5.3 HTTP — `PkgbuildService`, `SharedHttpClient`

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.3.1 | `SharedHttpClient.Instance` je `new HttpClient()` bez custom `HttpClientHandler` — **žiadne obchádzanie TLS certifikátovej validácie** (žiadny `ServerCertificateCustomValidationCallback`, žiadny `DangerousAcceptAnyServerCertificateValidator`). Predvolená .NET TLS validácia platí. | **OK** |
| 5.3.2 | **Cieľové hostitele sú hardcoded** (`aur.archlinux.org`, `gitlab.archlinux.org`) — `BuildUrl` v `PkgbuildService.cs:23-25` nedovoľuje útočníkovi presmerovať request na iný host, takže **klasický SSRF (arbitrary host) nehrozí**. | **OK** |
| 5.3.3 | `BuildUrl` napriek tomu **interpoluje `packageName` do URL bez `Uri.EscapeDataString`/URL-encodingu**: `$"https://aur.archlinux.org/cgit/aur.git/plain/PKGBUILD?h={packageName}"`. `packageName` pochádza z `Summary.Name`, čo je meno balíka získané parsovaním `yay -Ss`/`-Qi` výstupu — teda z AUR/repo katalógu, ktorý je (na rozdiel od official repo) čiastočne dôverou-neutrálny obsah, lebo AUR balíky môže pomenovať ktokoľvek (v praxi obmedzené AUR-side naming pravidlami na alfanumerické + `@ . _ + -`, ale aplikácia sa na to nespolieha ani to nevaliduje). Bez encodovania môže znak ako `?`, `&`, `#`, medzera alebo `%` v mene balíka zmeniť tvar requestu (napr. pridať/zmeniť query parameter na tom istom fixnom hoste) alebo spôsobiť zle sformovaný request. Riziko je nízke (fixný host, obmedzená znaková množina AUR mien), ale oprava je lacná: `Uri.EscapeDataString(packageName)`. | **MEDIUM** |
| 5.3.4 | `HttpClient` nemá explicitne nastavený `Timeout` (defaultných 100s platí) a `PkgbuildViewModel.ShowPkgbuildAsync` spúšťa `pkgbuild.LoadAsync().FireAndForget()` bez toho, aby zatvorenie modalu (`CloseCommand`) zrušilo prebiehajúci fetch cez `CancellationToken` — modal sa zavrie, ale HTTP request môže bežať ďalej na pozadí. Nejde o bezpečnostnú dieru, len o chýbajúcu cancellation-cestu (menší reliability/resource nález). | **LOW** |

### 5.4 Filesystem — `FolderBrowserService`, `FileSettingsStore`, `FolderBrowserViewModel`

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.4.1 | `FolderBrowserService.GetSubdirectories`/`DirectoryExists`/`GetParentPath` sú tenké wrappery nad `Directory.*`/`Path.*` bez vlastnej path-traversal logiky — **nie je to problém**, lebo folder browser je čisto lokálny UI nástroj nad **lokálnym filesystémom aktuálneho OS používateľa** (rovnaké oprávnenia ako proces sám), nie sandbox s nižšou dôverou. `../`-štýl traversal by len umožnil používateľovi navigovať do priečinkov, na ktoré má beztak OS-level prístup. Nie je tu trust-boundary porušenie. | **OK** |
| 5.4.2 | `FileSettingsStore` číta/zapisuje do `%APPDATA%/yay_see_sharp/settings.json` (resp. Linux ekvivalent `Environment.SpecialFolder.ApplicationData`) — žiadny user-controlled path vstup, žiadny traversal vektor. | **OK** |
| 5.4.3 | `BuildDirectory` (nastavenie AUR helper build adresára) sa nikde v predloženom kóde nepoužíva na skutočné spúšťanie buildu s user-controlled cestou vloženou do shell príkazu — je to len string v Settings modeli. Ak sa v budúcnosti použije ako `WorkingDirectory` pre `yay`, odporúčam pri zapájaní overiť, že cesta skutočne existuje a je zapisovateľná predtým, než sa odovzdá do `CommandRequest.WorkingDirectory` (dnes nie je zapojené, takže niet čo nájsť — len preventívna poznámka do budúcna). | **LOW** (informačné) |

### 5.5 Logging

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.5.1 | Jediné dve `Log()` volania v celom projekte (`NotifySendNotificationService.cs:44,52`) logujú iba exit code / exception message o `notify-send` zlyhaní — **žiadne heslo, token ani `CommandOutput` z privilegovaných operácií sa nikde neloguje**. | **OK** |
| 5.5.2 | `ErrorMessage = ex.Message` sa zobrazuje priamo v UI naprieč ViewModelmi (Search/Installed/PackageDetails/Dashboard) — pre lokálnu desktop appku bez remote logging/telemetry toto nie je information-disclosure riziko (žiadny externý pozorovateľ), len UX poznámka že raw `Exception.Message` (napr. z `HttpRequestException`) nie je vždy user-friendly. Mimo scope security review. | **OK** (mimo bezpečnostného scope) |

### 5.6 Secrets

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.6.1 | Grep na `api[_-]?key\|secret\|token=\|Bearer` v celom `source/` nenašiel žiadny hardcoded credential/API kľúč. Aplikácia nekomunikuje so žiadnou autentifikovanou treťou stranou (AUR/GitLab fetch je anonymný GET na verejný endpoint). | **OK** |

### 5.7 Process spawning — súhrn

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.7.1 | Všetkých 4 miesta, ktoré spúšťajú procesy (`SystemCommandRunner`, `ProcessSudoInvoker` ×2, `NotifySendNotificationService` cez `SystemCommandRunner`), používajú `ArgumentList` + `UseShellExecute = false` + `CreateNoWindow = true`. Konzistentné naprieč celým projektom. | **OK** |

### 5.8 Trust boundary

| # | Zistenie | Závažnosť |
|---|---|---|
| 5.8.1 | Trust boundary je vo veľkej miere jasná: AUR package names/search queries sú "vonkajší" (community-controlled/user-typed) vstup, ktorý ide len do `ArgumentList` (bezpečné) alebo neescapovaného URL stringu (5.3.3, MEDIUM) — nikdy do shellu. Lokálny filesystém a lokálne `sudo`/`yay` binárky sú "vlastný" trust boundary (rovnaké oprávnenia ako spúšťajúci používateľ), takže path-traversal v `FolderBrowserService` (5.4.1) správne nie je liečený ako hrozba. | **OK** s výhradou 5.2.2/5.3.3 |

---

## Záver

**Čo je dobré:**
- Sudo/privilege flow je bezpečnostne najzrelšia časť kódu: žiadne heslo v argumentoch, logoch ani perzistencii; fail-closed default; solídne testy.
- Kompletná absencia shell string interpolácie — každý proces sa spúšťa cez `ArgumentList`.
- Predchádzajúcich 12 findingov z `code-review-findings.md` je reálne, overiteľne opravených (nie len "papierovo" v dokumentácii) — vrátane `removeOrphans` prepojenia, ktoré bolo predtým tichou logickou chybou s reálnym dopadom (odstránenie viac balíkov než používateľ chcel).
- Kontraktné testy Demo↔Yay backendu (LSP) sú nadštandardná praktika pre projekt tejto veľkosti.
- Views sú dôsledne "hlúpe" — žiadna business logika, žiadne priame volanie systémových príkazov z UI vrstvy.

**Čo riešiť prednostne (v poradí):**
1. **`Uri.EscapeDataString(packageName)`** v `PkgbuildService.BuildUrl` (5.3.3) — lacná oprava, MEDIUM.
2. **Argument/flag-injection hardening** pre `yay -Ss <query>` a package name argumenty — buď `--` separator pred pozičnými argumentmi, alebo validácia že vstup nezačína `-` (5.2.2) — MEDIUM.
3. **Rozpletenie `MainWindowViewModel` z rolí composition-root + dashboard ViewModel** (2.3) — presunúť inštanciáciu `SettingsAwareNotificationService`/`NotifySendNotificationService`/`SystemCommandRunner` do `App.axaml.cs` popri zvyšku composition rootu, a odovzdať hotové inštancie cez konštruktor.
4. Zvážiť malý "architecture test" (napr. jeden unit test, ktorý reflection-om overí, že `Domain.dll`/namespace nereferencuje `Avalonia`), aby 1.3 prestalo byť len disciplínou a stalo sa vynútiteľným.
5. DRY refaktor `IsBusy`/operation-lifecycle duplikácie (4.1, 4.2) — nie urgentné, ale zníži riziko budúcich nekonzistentných opráv.

Žiadny CRITICAL alebo neopravený HIGH nález. Projekt je v stave, kde ďalšie zlepšenia sú o zlepšovaní už dobrého základu, nie o hasení dier.
