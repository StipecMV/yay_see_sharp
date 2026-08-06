# yay_see_sharp — Code Review Findings

**Projekt:** `/home/hp-camera-hub/workspace/yay_see_sharp`
**Účel dokumentu:** Aktuálny backlog nálezov pre implementačného agenta. Dokument obsahuje iba nálezy, ktoré treba ešte overiť alebo opraviť, plus register už vyriešených starších nálezov, aby sa neopravovali znova.
**Rozsah:** aktuálny working tree vrátane untracked súborov, nie iba `git diff`.
**Review typ:** code review, architecture/MVVM review, cybersecurity review, documentation review.
**Dátum review:** 2026-08-04.

---

## 0. Stav projektu pri review

### Reálne overený build

```text
dotnet --version
10.0.110

dotnet build yay_see_sharp.slnx --configuration Debug --no-restore
Build succeeded.
0 Warning(s)
0 Error(s)
```

### Reálne overené testy

| Projekt | Passed | Failed | Skipped |
|---|---:|---:|---:|
| `tests/yay_see_sharp.domain.Tests` | 2 | 0 | 0 |
| `tests/yay_see_sharp.infrastructure.Tests` | 86 | 0 | 0 |
| `tests/yay_see_sharp.application.Tests` | 81 | 0 | 0 |
| `tests/yay_see_sharp.integration.Tests` | 11 | 0 | 3 |
| `tests/yay_see_sharp.e2e.Tests` | 8 | 0 | 0 |
| **Spolu** | **188** | **0** | **3** |

Tri skipped testy sú deštruktívne Arch/CachyOS testy a vyžadujú:

```text
YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS=1
```

na reálnom Arch Linux/CachyOS hoste s `yay` na `PATH`.

### Dependency security scan

Reálne overené:

```bash
dotnet list yay_see_sharp.slnx package --vulnerable --include-transitive
```

Výsledok: všetky projekty hlásili **no vulnerable packages**.

### Dôležitá poznámka k interpretácii testov

Passing unit, integration alebo headless E2E testy neznamenajú, že je overený skutočný Arch runtime, desktop tray, D-Bus notifikácie, interaktívny sudo prompt alebo vizuálny vzhľad. Tieto časti treba overiť samostatne na hoste s GUI.

---

# 1. Otvorené nálezy podľa priority

---

## FINDING-01 — Arch/CachyOS sa označí ako Real mode aj bez nainštalovaného `yay`

**Závažnosť:** HIGH
**Typ:** potvrdený funkčný/runtime problém
**Oblasť:** backend detection, startup wiring, error handling

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Platform/DistributionDetector.cs:51-62
source/yay_see_sharp.infrastructure/PackageBackendFactory.cs:29-41
source/yay_see_sharp.infrastructure/Platform/EngineDetector.cs:20-32
```

### Aktuálny stav

`LinuxDistributionDetector.CreateBackendInfo()` rozhoduje iba podľa distribúcie:

```csharp
var isArch = distribution.Id is "arch" or "cachyos";

return isArch
    ? new BackendInfo(distribution.Id, distribution.Name, "yay", BackendMode.Real, true)
    : ...;
```

Na Arch/CachyOS sa teda zvolí Real mode bez overenia, či:

- `yay` existuje na `PATH`,
- je spustiteľný,
- sa dá spustiť a vráti použiteľný exit code.

`EngineDetector` síce existuje a kontroluje `yay`/`paru`, ale `PackageBackendFactory` ho nepoužíva pri runtime výbere backendu. Factory vždy vytvorí `YayPackageBackend` pre Arch/CachyOS.

### Prečo je to problém

Product requirements hovoria, že Real mode platí pre Arch/CachyOS **s nainštalovaným `yay`**. Na čistom Arch systéme bez `yay` aplikácia zobrazí Real mode a až následne zlyhá pri prvom volaní `yay`. Používateľ nedostane správny stav ani jasné odporúčanie, čo má nainštalovať.

Toto môže ovplyvniť aj startup refresh, pretože dashboard začne volať `yay` hneď po vytvorení ViewModelu.

### Návrh opravy

Vyberte a zdokumentujte jednu konzistentnú stratégiu:

1. Detection musí overiť distribúciu **aj dostupnosť `yay`**.
2. Ak je Arch/CachyOS bez `yay`, backend nesmie byť označený ako použiteľný Real mode.
3. Použite explicitný stav, napríklad `BackendMode.Unavailable`/`BackendAvailability`, alebo bezpečný Demo mode s jasným warningom. Demo mode však nesmie používateľa presvedčiť, že operácie menia hostiteľský package database.
4. Startup UI musí zobraziť presnú príčinu: `yay` nebol nájdený na `PATH`.
5. Ak sa implementuje requirement na inštaláciu chýbajúceho recommended backendu, musí ísť o explicitnú potvrdenú akciu s presným príkazom a bezpečným sudo flow.
6. `EngineDetector` a backend factory musia používať rovnaký zdroj pravdy; nesmie existovať detection helper, ktorý runtime ignoruje.

### Povinné testy

- Arch + `yay` na PATH → Real `YayPackageBackend`.
- Arch + `yay` mimo PATH → nie Real-ready backend; správny warning/stav.
- CachyOS + `yay` na PATH → Real backend.
- Ubuntu/Debian/Fedora → Demo backend.
- `yay` binary existuje, ale nie je spustiteľný alebo command zlyhá → bezpečný error stav.
- Startup bez `yay` nesmie spadnúť na nepozorovanej výnimke.

### Akceptačné kritériá

- Real mode sa zobrazí iba vtedy, keď sú splnené všetky jeho prerequisites.
- Aplikácia nikdy nepotichu nespúšťa `yay`, ak detection nevie potvrdiť jeho dostupnosť.
- README a implementation status presne opisujú správanie pri chýbajúcom `yay`.

---

## FINDING-02 — Real `YayPackageBackend` vracia neúplné a zavádzajúce štatistiky

**Závažnosť:** HIGH
**Typ:** potvrdený functional completeness problém
**Oblasť:** real backend, dashboard, product requirements

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.cs:107-131
```

### Aktuálny stav

Backend spustí iba:

```csharp
new CommandRequest("yay", ["-Qq"])
```

a potom vytvorí:

```csharp
return new PackageStatistics(
    installedCount,
    0,
    0,
    0,
    0,
    0,
    0,
    null);
```

Reálne sa počíta iba `InstalledCount`. Vždy neúplné alebo nulové sú:

- `ExplicitCount`,
- `DependencyCount`,
- `AurCount`,
- `UpdatesAvailable`,
- `InstalledSizeBytes`,
- `OrphanCount`,
- `LastUpdateCheck`.

### Prečo je to problém

Dashboard v Real mode môže používateľovi zobrazovať nuly, ktoré neznamenajú skutočný stav systému. `docs/product-requirements.md` výslovne požaduje všetky uvedené štatistiky. Existujúci test overuje iba `InstalledCount`, takže túto neúplnosť nezachytí.

Toto nie je iba chýbajúca optimalizácia — ide o rozdiel medzi deklarovanou feature a runtime výsledkom.

### Návrh opravy

Implementujte real statistics cez explicitné, testovateľné query operácie, napríklad:

- `yay -Q`/`pacman -Q` pre package names, versions a veľkosť,
- `pacman -Qe` pre explicitne nainštalované balíky,
- `pacman -Qd` pre dependency-installed balíky,
- vhodný `pacman`/`yay` query pre foreign/AUR balíky,
- `pacman -Qu` alebo konzistentný `yay` update query pre updates,
- `pacman -Qdt` pre orphan dependencies,
- veľkosť parsovať z `-Qi` alebo použiť spoľahlivé package metadata.

Preferujte jednu query service/parser vrstvu namiesto náhodného počítania riadkov z rôznych textových výstupov. Ak určitý údaj nie je možné spoľahlivo získať, model/UI musí zobrazovať `Unknown`/`Not available`, nie falošnú nulu.

### Povinné testy

- parser a aggregation test s explicitnými, dependency, AUR a orphan balíkmi,
- updates available > 0,
- nulové updates,
- installed size parsing,
- správny `LastUpdateCheck` po úspešnom update checku,
- zlyhanie jednej query nesmie vytvoriť štatistiky, ktoré vyzerajú ako platné nuly,
- reálny gated Arch test overujúci aspoň základné štatistiky.

### Akceptačné kritériá

- Real dashboard nepoužíva hardcoded nuly pre údaje, ktoré product requirements označujú ako podporované.
- Každý field v `PackageStatistics` má buď reálny zdroj, alebo explicitný unknown stav.
- Testy overujú viac než iba počet nainštalovaných balíkov.

---

## FINDING-03 — `GetDetailsAsync()` používa iba `yay -Qi`, takže nenainštalované balíky nemajú detaily

**Závažnosť:** MEDIUM
**Typ:** potvrdený functional problém
**Oblasť:** search → package details flow

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.cs:86-104
source/yay_see_sharp.application/ViewModels/PackageDetailsViewModel.cs:133-149
source/yay_see_sharp.application/Views/PackageDetailsView.axaml:39-45
```

### Aktuálny stav

Real backend používa:

```csharp
new CommandRequest("yay", ["-Qi", packageName.Trim()])
```

`-Qi` je query nainštalovaného balíka. Search však vracia aj nenainštalované balíky a UI ich posiela do rovnakého detail flow. Pri nenainštalovanom balíku backend typicky vráti `null`.

### Prečo je to problém

Používateľ môže balík vyhľadať, ale package detail obrazovka potom nemá dostupné údaje ako:

- detailný description,
- maintainer,
- dependencies,
- homepage,
- size.

Tlačidlo Install môže existovať, ale detail view je neúplný. To porušuje požiadavku na zobrazovanie detailov pre search výsledky, nie iba pre už nainštalované balíky.

### Návrh opravy

- Pre nainštalovaný balík používajte query typu `-Qi`.
- Pre nenainštalovaný balík používajte vhodnú sync/info query, napríklad `-Si`, alebo iný overený `yay` query režim.
- Ak prvý query zlyhá, použite explicitný fallback podľa známeho stavu balíka.
- Parser musí odlišovať installed a repository/AUR metadata.
- Nesmie sa automaticky spúšťať install iba kvôli získaniu detailov.

### Povinné testy

- detail nainštalovaného balíka → správna `-Qi` command cesta,
- detail nenainštalovaného official balíka → repository info query,
- detail nenainštalovaného AUR balíka → AUR info query,
- chýbajúci balík → `null`/user-friendly error,
- cancellation a non-zero exit code.

### Akceptačné kritériá

- Detail view pre search result zobrazí údaje aj pred inštaláciou, ak ich backend poskytuje.
- Command path je pokrytý presnými `ICommandRunner` testami.

---

## FINDING-04 — AUR/Official source sa v Real mode nesprávne klasifikuje

**Závažnosť:** MEDIUM
**Typ:** potvrdený data correctness problém
**Oblasť:** parser, filters, statistics, UI labels

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Yay/YayOutputParser.cs:83-123
```

### Aktuálny stav

`ParseInstalled()` vytvára každý package ako:

```csharp
PackageSource.Official
```

`ParseUpdates()` vytvára každý update ako:

```csharp
PackageSource.Official
```

### Prečo je to problém

V reálnom systéme môžu byť nainštalované aj AUR/foreign balíky. Nesprávna klasifikácia ovplyvní:

- Official/AUR filter a tagy,
- AUR count v štatistikách,
- Installed screen,
- source pri update položkách,
- následné rozhodovanie o AUR PKGBUILD URL.

Demo backend tento problém nemá, preto prechodné Demo testy nie sú dostatočné.

### Návrh opravy

- Použite explicitný query/parser pre foreign/AUR balíky alebo porovnanie s repository metadata.
- Source classification centralizujte do jednej služby, aby `ParseInstalled`, `ParseUpdates` a details flow nepoužívali odlišné pravidlá.
- Ak source nie je možné spoľahlivo určiť, používajte `Unknown`/neznámy stav namiesto nesprávneho `Official`; ak model zatiaľ `Unknown` nemá, rozšírte ho.
- Zachovajte správne oddelenie AUR PKGBUILD endpointu a official packaging endpointu.

### Povinné testy

- official installed package,
- foreign/AUR installed package,
- official update,
- AUR update,
- source filtering a UI tagy,
- real gated Arch verification s balíkom z AUR, ak je bezpečne dostupný.

### Akceptačné kritériá

- AUR balík sa v Real mode nikdy automaticky neoznačí ako Official iba preto, že pochádza z `-Q`/`-Qu` outputu.
- Statistics a UI používajú rovnaké source pravidlo.

---

## FINDING-05 — Scheduler interpretuje používateľský čas ako UTC, nie lokálny čas

**Závažnosť:** MEDIUM
**Typ:** potvrdený functional/time-zone problém
**Oblasť:** scheduling, user settings

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Platform/SystemClock.cs:5-7
source/yay_see_sharp.infrastructure/Scheduling/UpdateScheduler.cs:107-133
source/yay_see_sharp.domain/Scheduling/UpdateScheduleCalculator.cs:5-12
```

### Aktuálny stav

System clock vracia:

```csharp
public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
```

`UpdateScheduleTime` je používateľské `TimeOnly`, ale scheduler ho aplikuje voči UTC času. Nastavenie `10:00` preto na Slovensku počas letného času typicky spustí kontrolu o 12:00 lokálne, nie o 10:00.

Existujúce scheduler testy používajú `TimeSpan.Zero`, takže rozdiel medzi UTC a lokálnym časom neodhalia.

### Prečo je to problém

UI a requirements hovoria o dennej kontrole v nastavenom čase. Používateľ očakáva lokálny čas svojho desktop session, nie UTC, pokiaľ to nie je výslovne uvedené.

### Návrh opravy

Vyberte a zdokumentujte časový model. Odporúčaná implementácia:

1. `IClock` nech poskytuje aktuálny čas v konkrétnej timezone alebo `LocalNow`.
2. Scheduler nech interpretuje `TimeOnly` v lokálnej timezone používateľa.
3. Interné porovnanie môže byť v UTC, ale konverzia musí byť explicitná a správna.
4. Zohľadnite zmenu letného/zimného času.
5. Pri zmene schedule alebo enable toggle okamžite invalidujte cached `NextScheduledRun`.

### Povinné testy

- timezone UTC,
- timezone `Europe/Bratislava` v zimnom čase,
- timezone `Europe/Bratislava` v letnom čase,
- schedule pred/po aktuálnom čase,
- zmena nastaveného času počas behu,
- vypnutie a opätovné zapnutie scheduleru.

### Akceptačné kritériá

- Nastavenie `10:00` znamená 10:00 v deklarovanej user timezone.
- README, requirements a implementation status uvádzajú rovnaký timezone model.

---

## FINDING-06 — Single-instance služba neaktivuje existujúcu aplikáciu

**Závažnosť:** MEDIUM
**Typ:** potvrdený feature/integration problém
**Oblasť:** single instance, tray, application lifecycle

### Lokalizácia

```text
source/yay_see_sharp.domain/Abstractions/ISingleInstanceService.cs:1-6
source/yay_see_sharp.infrastructure/Platform/FileLockSingleInstanceService.cs:5-52
source/yay_see_sharp.application/App.axaml.cs:27-37
```

### Aktuálny stav

Interface obsahuje iba:

```csharp
bool TryAcquire();
```

Ak druhá instancia lock nezíska, `App.axaml.cs` ju iba ukončí:

```csharp
if (composition is null)
{
    desktop.Shutdown();
    return;
}
```

Neexistuje IPC/activation message, event alebo iný mechanizmus na informovanie prvej inštancie.

### Prečo je to problém

Product requirements hovoria, že druhé spustenie má aktivovať existujúcu instanciu. Aktuálne druhé spustenie síce pravdepodobne neotvorí druhé okno, ale neobnoví skryté okno z tray a neaktivuje existujúcu aplikáciu.

### Návrh opravy

- Rozšírte `ISingleInstanceService` o activation transport, napríklad `TryActivateExisting()` a event/receiver v prvej instancii.
- Použite per-user IPC, napríklad Unix domain socket alebo bezpečne vytvorený local socket/named pipe podľa platformy.
- Druhá instancia po zistení locku odošle activation message a skončí.
- Prvá instancia pri prijatí message zavolá tray/window restore a focus.
- IPC endpoint musí byť per-user a chránený pred cross-user spoofingom.
- Lifecycle musí korektne zavrieť receiver pri exit.

### Povinné testy

- prvá instancia získa lock,
- druhá instancia lock nezíska,
- druhá instancia odošle activation,
- prvá instancia prijme activation,
- hidden-to-tray window sa obnoví,
- receiver sa ukončí pri app exit,
- cross-user/permission failure je bezpečne spracovaný.

### Akceptačné kritériá

- Druhé spustenie nikdy nevytvorí druhú app session.
- Existujúca hidden/tray instancia sa po druhom spustení zobrazí a aktivuje.

---

## FINDING-07 — Predvídateľný globálny lock file v `/tmp`

**Závažnosť:** MEDIUM
**Typ:** cybersecurity hardening / local denial-of-service risk
**Oblasť:** single-instance security

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Platform/FileLockSingleInstanceService.cs:10-15
```

### Aktuálny stav

Default path je:

```csharp
Path.Combine(Path.GetTempPath(), "yay_see_sharp.lock");
```

### Prečo je to problém

Globálny predvídateľný súbor v `/tmp` môže na multi-user hoste umožniť inému používateľovi:

- vytvoriť súbor vopred,
- zabrániť štartu aplikácie,
- pokúsiť sa ovplyvniť single-instance flow.

Nie je to privilege escalation, ale lokálny DoS a nedostatočné oddelenie používateľov.

Ďalší problém je mazanie lock path v `Dispose()`: aplikácia by nemala mazať objekt, ktorý môže po race/recovery patriť inej instancii.

### Návrh opravy

- Použite per-user runtime/config directory, napríklad `$XDG_RUNTIME_DIR` s per-user oprávneniami, alebo platformový user-specific application data path.
- Directory vytvorte s bezpečnými permissions.
- Použite atomic create/open semantics.
- Lock file po dispose nemažte, ak to nie je nevyhnutné; filesystem lock sa po zatvorení streamu uvoľní.
- Ak path alebo permission zlyhá, UI má zobraziť konkrétny recoverable error.

### Povinné testy

- dva procesy toho istého používateľa,
- dva rôzni používatelia s odlišnými lock paths,
- pre-existing path,
- permission denied,
- crash/restart recovery,
- dispose jedného procesu nesmie odomknúť živú druhú instanciu.

### Akceptačné kritériá

- Lock nie je globálny medzi používateľmi.
- Iný používateľ nemôže jednoduchým vytvorením predvídateľného `/tmp/yay_see_sharp.lock` zablokovať aplikáciu.

---

## FINDING-08 — ViewModels stále priamo vytvárajú Infrastructure implementácie

**Závažnosť:** MEDIUM
**Typ:** architecture/DIP/SRP odchýlka
**Oblasť:** composition root, MVVM

### Lokalizácia

```text
source/yay_see_sharp.application/ViewModels/SettingsViewModel.cs:31-40
source/yay_see_sharp.application/ViewModels/FolderBrowserViewModel.cs:24-29
source/yay_see_sharp.application/ViewModels/PkgbuildViewModel.cs:25-31
source/yay_see_sharp.application/ViewModels/DashboardViewModel.cs:22-27
source/yay_see_sharp.application/ViewModels/PackageDetailsViewModel.cs:22-35
```

### Aktuálny stav

Aj keď `AppBootstrapper` existuje, production defaults v application ViewModels stále obsahujú napríklad:

```csharp
new EngineDetector()
new FolderBrowserService()
new PkgbuildService()
NullNotificationService.Instance
```

ViewModels preto priamo referencujú Infrastructure namespaces a vytvárajú konkrétne implementácie.

### Prečo je to problém

Dokumentácia tvrdí, že `AppBootstrapper` je jediný composition root. V skutočnosti je construction rozdelená medzi bootstrapper a ViewModels.

Dôsledky:

- porušenie Dependency Inversion,
- skrytý rozdiel medzi production a test wiring,
- zložitejšia výmena implementácie,
- väčšia väzba application assembly na Infrastructure,
- vyššie riziko, že nový ViewModel začne robiť platformové rozhodnutia sám.

### Návrh opravy

- Production dependency vytvárajte v `AppBootstrapper`.
- Konštruktory ViewModels nech prijímajú abstrakcie alebo hotové služby bez implicitných concrete defaults.
- Ak sú convenience constructors potrebné pre design-time/test, oddeľte ich od production constructorov alebo používajte explicitný test factory.
- `NullNotificationService` môže zostať ako vedomý no-op v test factory, nie ako skrytý production fallback bez zdokumentovania.
- Po refaktore aktualizujte architecture diagram a README.

### Povinné testy

- application tests vytvoria ViewModels iba s fake/mock abstractions,
- architecture test/reflection check overí, že ViewModels neimportujú konkrétne Infrastructure namespaces,
- bootstrapper smoke test overí production wiring.

### Akceptačné kritériá

- `AppBootstrapper` je jediný production composition root.
- ViewModels neobsahujú `new` konkrétnych Infrastructure služieb.
- UI správanie sa nezmení a všetky testy zostanú zelené.

---

## FINDING-09 — `BuildDirectory` je iba uložené nastavenie bez runtime consumeru

**Závažnosť:** MEDIUM
**Typ:** confirmed missing integration / YAGNI risk
**Oblasť:** settings, AUR build flow

### Lokalizácia

```text
source/yay_see_sharp.domain/Models/SettingsModels.cs:15-24
source/yay_see_sharp.application/ViewModels/SettingsViewModel.cs:193-200
source/yay_see_sharp.application/ViewModels/SettingsViewModel.cs:307-317
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend*.cs
```

### Aktuálny stav

Settings umožňuje:

- zobraziť `BuildDirectory`,
- vybrať cestu cez custom folder browser,
- persistovať ju do settings JSON.

V runtime `YayPackageBackend` však toto nastavenie nepoužíva ako `WorkingDirectory` ani ho neposiela do `CommandRequest` pri package/AUR operáciách.

### Prečo je to problém

Používateľ nastaví hodnotu, ktorá sa uloží, ale nemení správanie aplikácie. To je UI-only/persisted-only feature a porušenie YAGNI/principle of least surprise.

### Návrh opravy

Vyberte jednu možnosť:

**Možnosť A — implementovať:**

- preniesť build directory cez domain/application policy do backendu,
- expandovať `~` bezpečne na user home,
- overiť existenciu, ownership, permissions a writability,
- používať ho ako working directory iba tam, kde to `yay` skutočne podporuje,
- nezasúvať cestu do shell stringu,
- správne riešiť cancellation a cleanup.

**Možnosť B — odstrániť z MVP:**

- odstrániť setting a folder browser z UI/modelu,
- ponechať ho ako future feature iba v dokumentácii.

### Povinné testy pri implementácii

- persisted path sa dostane do runtime consumeru,
- `~` expansion,
- path s medzerami,
- missing/not writable directory,
- cancellation cleanup,
- path nikdy nejde cez shell interpolation.

### Akceptačné kritériá

- Každé zobrazené nastavenie mení runtime správanie, alebo je z UI odstránené.

---

## FINDING-10 — Requirement na explicitnú inštaláciu chýbajúceho recommended backendu nie je zapojený

**Závažnosť:** MEDIUM
**Typ:** confirmed missing product requirement
**Oblasť:** backend availability, privileged operation UX

### Lokalizácia

```text
docs/product-requirements.md:34-35
source/yay_see_sharp.infrastructure/Platform/DistributionDetector.cs
source/yay_see_sharp.infrastructure/PackageBackendFactory.cs
source/yay_see_sharp.application/
```

### Aktuálny stav

Product requirements požadujú možnosť ponúknuť explicitnú, potvrdenú inštaláciu missing recommended backendu. V aktuálnom kóde som nenašiel:

- ViewModel command pre install backendu,
- confirmation dialog/flow,
- exact command preview,
- backend installation service,
- test pre cancellation/failure/success.

### Prečo je to problém

Toto je deklarovaná MVP capability, ale nie je runtime implementovaná. Nález FINDING-01 navyše znamená, že práve na Arch bez `yay` aplikácia nevie používateľovi pomôcť korektným flow.

### Návrh opravy

Ak má feature zostať v MVP:

1. Backend availability musí vrátiť recommended command podľa distribúcie.
2. UI musí zobraziť presný command a vyžiadať explicitné potvrdenie.
3. Installation service musí používať `ArgumentList`, nie shell string.
4. Privileged installation musí prejsť cez schválený `IPrivilegeService`/sudo flow.
5. Heslo nesmie ísť do argv, logov ani persistence.
6. Po inštalácii sa musí zopakovať detection a backend selection.
7. Pri zlyhaní musí zostať aplikácia v bezpečnom unavailable stave.

Ak sa feature odkladá mimo MVP, odstráňte ju z product requirements/README alebo ju označte ako future feature.

### Povinné testy

- missing backend → recommendation visible,
- command preview zodpovedá skutočným argumentom,
- cancel confirmation → žiadny process,
- sudo cancel/failure,
- command failure,
- successful install → re-detection,
- no password in `CommandRequest.Arguments` ani logs.

### Akceptačné kritériá

- Requirement je buď reálne implementovaný a testovaný, alebo je transparentne presunutý mimo MVP.

---

## FINDING-11 — Argument/flag injection hardening pre package/query argumenty

**Závažnosť:** LOW/MEDIUM
**Typ:** cybersecurity hardening; nie shell injection
**Oblasť:** process execution, `yay` CLI arguments

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.cs:60-83
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.cs:86-104
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.Install.cs:37-39
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.Uninstall.cs:39-41
source/yay_see_sharp.infrastructure/Yay/YayPackageBackend.Update.cs:12-35
```

### Aktuálny stav

Používateľský search query a package names idú do `ArgumentList`, čo správne chráni pred shell injection. Napriek tomu sa hodnoty neposudzujú proti tomu, či začínajú `-` a môžu byť interpretované ako CLI options pre `yay`/pacman.

### Prečo je to problém

Toto nie je klasické OS command injection — nový shell sa nespúšťa. Je to však argument confusion/option injection. Text ako `--some-option` môže zmeniť význam command invocation.

Package names z výsledkov trusted parsera majú užší formát, ale aplikácia by sa na neho nemala spoliehať bez validácie.

### Návrh opravy

- Oddeliť search query od package-name validácie.
- Pre package names použiť whitelist formát kompatibilný s Arch package naming rules; odmietnuť prázdne hodnoty a hodnoty začínajúce `-`.
- Overiť, či `yay` podporuje `--` na miestach, kde sa odovzdávajú pozičné argumenty, a použiť ho tam, kde je syntakticky správne.
- Nepridávať `--` slepo pred `-S`/`-R` operation flags — command syntax musí byť otestovaná s reálnym `yay`.
- Query string môže obsahovať širší text, ale nesmie byť použitý ako command option bez explicitnej kontroly.

### Povinné testy

- search query začínajúci `-`,
- package name začínajúci `-`,
- package name s medzerou/invalid znakom,
- normálne AUR names s `-`, `.`, `_`, `+`, `@`,
- presné `ArgumentList` testy,
- gated real `yay` smoke test.

### Akceptačné kritériá

- Žiadny user-controlled value nemôže neúmyselne zmeniť command flags.
- Naďalej sa používa `ArgumentList`; nesmie vzniknúť shell command string.

---

## FINDING-12 — Sudo child process nemá explicitnú cancellation/termination cestu

**Závažnosť:** LOW
**Typ:** reliability/security hardening
**Oblasť:** sudo authentication

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Privilege/ProcessSudoInvoker.cs:20-33
```

### Aktuálny stav

`RefreshWithPasswordAsync()` pri cancellation čaká na `WaitForExitAsync(cancellationToken)`, ale explicitne nekilluje sudo process. `using` process síce dispose-ne, ale cancellation lifecycle nie je rovnako explicitný ako v `SystemCommandRunner`.

### Prečo je to problém

Sudo command je krátky, preto ide o nízke praktické riziko. Napriek tomu môže pri hanging PAM/helper stave zostať child process alebo otvorený pipe dlhšie než operation, najmä pri zatváraní aplikácie alebo zrušení promptu.

### Návrh opravy

- Pri cancellation zavolať bezpečný terminate/kill process.
- Zavrieť stdin bez ponechania hesla v pipe.
- Await-nuť process shutdown a swallow-núť iba očakávané cancellation/exit chyby.
- Zachovať fakt, že heslo nejde do argv ani logu.
- Zvážiť timeout pre sudo validation/refresh.

### Povinné testy

- cancellation pred startom,
- cancellation počas sudo wait,
- process cleanup,
- password never in arguments/log output,
- failed sudo exit code.

### Akceptačné kritériá

- Zrušenie auth operation ukončí súvisiaci child process a neblokuje shutdown.

---

## FINDING-13 — PKGBUILD fetch sa po zatvorení modalu nemusí zrušiť

**Závažnosť:** LOW
**Typ:** reliability/resource lifecycle
**Oblasť:** HTTP, modal lifecycle

### Lokalizácia

```text
source/yay_see_sharp.application/ViewModels/PkgbuildViewModel.cs:60-82
source/yay_see_sharp.application/ViewModels/PkgbuildViewModel.cs:90
source/yay_see_sharp.application/ViewModels/PackageDetailsViewModel.cs:219-225
source/yay_see_sharp.infrastructure/Http/SharedHttpClient.cs:5-9
```

### Aktuálny stav

`PackageDetailsViewModel.ShowPkgbuildAsync()` spustí:

```csharp
pkgbuild.LoadAsync().FireAndForget();
await pkgbuild.WaitForCloseAsync();
Pkgbuild = null;
```

`CloseCommand` iba dokončí `TaskCompletionSource`; necanceluje HTTP request. Modal sa môže zatvoriť, ale request pokračuje na pozadí.

### Prečo je to problém

Nie je to SSRF ani credential issue, ale:

- zbytočne pokračuje network request,
- neskorá response môže meniť už nepoužívaný ViewModel,
- pri opakovanom otváraní sa môžu hromadiť requesty,
- shutdown/cancellation semantics nie sú jasné.

### Návrh opravy

- `PkgbuildViewModel` nech vlastní `CancellationTokenSource`.
- `Close()` nech cancelne fetch a dokončí close task.
- `LoadAsync()` nech odlíši expected cancellation od skutočnej chyby.
- Pri dispose/close korektne zrušiť request.
- `HttpClient.Timeout` nastaviť explicitne na rozumnú hodnotu.

### Povinné testy

- close počas fetchu,
- cancellation nehlási error ako failed fetch,
- timeout,
- HTTP 404/500,
- successful fetch,
- opakované open/close bez leak-like background taskov.

### Akceptačné kritériá

- Po zatvorení modalu už PKGBUILD fetch nepokračuje.
- HTTP failure a user cancellation sú v UI odlíšené.

---

## FINDING-14 — Avalonia/ReactiveUI package family stále obsahuje cross-major risk

**Závažnosť:** LOW
**Typ:** dependency architecture risk; nie potvrdená runtime chyba
**Oblasť:** NuGet dependency management

### Lokalizácia

```text
Directory.Packages.props:12-15
source/yay_see_sharp.application/yay_see_sharp.application.csproj:37-50
```

### Aktuálny stav

Overený package graph application projektu obsahuje:

```text
Avalonia                 12.1.1
Avalonia.Desktop         12.1.1
Avalonia.Themes.Fluent   12.1.1
Avalonia.ReactiveUI      11.3.9
ReactiveUI               20.1.1 (transitive)
```

`Avalonia.Diagnostics` 11.x bolo odstránené, čo je správne. Zostáva však Avalonia core 12.x spolu s `Avalonia.ReactiveUI` 11.3.9.

Predchádzajúce review označilo tento mix za HIGH, ale aktuálny stav treba hodnotiť presnejšie: build a testy prešli a nebol preukázaný konkrétny runtime failure. Ide preto o residual compatibility risk, nie potvrdený bug.

### Prečo je to problém

Cross-major integration package môže pri budúcej zmene alebo konkrétnom runtime path spôsobiť:

- API/runtime incompatibility,
- rozdiely medzi Debug/Release alebo platformami,
- ťažšie upgradeovanie,
- nejasný dependency graph.

### Návrh opravy

- Overiť oficiálnu podporovanú Avalonia 12 ReactiveUI integráciu a package metadata cez NuGet.
- Nepoužiť balík iba podľa podobného názvu bez overenia dependencies a API.
- Ak kompatibilná 12.x integration package existuje a je stabilná, migrovať na ňu.
- Ak neexistuje bezpečná náhrada, ponechať current shim iba s explicitným architectural decision record a regression testom; nevymýšľať neoverenú verziu.
- Zachovať `Directory.Packages.props` ako jediný zdroj priamych verzií.

### Povinné testy

- `dotnet list ... package --include-transitive` pre application aj e2e,
- Debug build,
- Release build,
- headless E2E,
- startup smoke na Linuxe.

### Akceptačné kritériá

- Package family je buď podporovane jednotná, alebo existuje zdokumentované a overené rozhodnutie pre residual shim.
- Žiadny nový `Avalonia.Diagnostics` 11.x sa nevráti do projektu.

---

# 2. Dokumentačné nálezy

---

## DOC-01 — `implementation-status.md` obsahuje zastarané project paths a test counts

**Závažnosť:** HIGH
**Typ:** documentation drift

### Lokalizácia

```text
docs/implementation-status.md:5-10
docs/implementation-status.md:68-73
```

### Aktuálny stav

Dokument stále uvádza staré projekty:

```text
tests/yay_see_sharp.unittests
tests/yay_see_sharp.integrationtests
```

a starý stav:

```text
162 unit tests
14 integration tests
```

Aktuálne solution obsahuje:

```text
tests/yay_see_sharp.domain.Tests
tests/yay_see_sharp.infrastructure.Tests
tests/yay_see_sharp.application.Tests
tests/yay_see_sharp.integration.Tests
tests/yay_see_sharp.e2e.Tests
```

Aktuálne reálne overené počty sú:

```text
188 passed
0 failed
3 skipped
```

### Prečo je to problém

Implementačný agent môže spúšťať neexistujúce príkazy, vyhodnocovať nesprávny test count alebo označiť feature ako overenú na základe starej dokumentácie.

### Návrh opravy

- Aktualizovať project paths a test table podľa aktuálneho solution.
- Uviesť e2e test project.
- Oddeliť passed/failed/skipped.
- Uviesť presný príkaz pre každý projekt.
- Uviesť dátum poslednej verifikácie.
- Nepísať „Hotové“ iba na základe existencie ViewModelu/interface; uviesť runtime scope a test limitations.

### Akceptačné kritériá

- Každý command v dokumente funguje v aktuálnom checkout.
- Počty sú reprodukovateľné spustením uvedených commands.

---

## DOC-02 — Historický URL-encoding nález bol superseded a pôvodný self-review súbor bol odstránený

**Závažnosť:** LOW
**Typ:** documentation drift / historical record

### Lokalizácia

```text
docs/self-review.md — súbor bol odstránený
source/yay_see_sharp.infrastructure/Http/PkgbuildService.cs:23-29
```

### Aktuálny stav

Pôvodný self-review uvádzal URL encoding ako otvorený problém a odporúčal `Uri.EscapeDataString(packageName)`. Súbor `docs/self-review.md` už bol odstránený, preto staré lokalizácie v tomto review dokumente nesmú byť interpretované ako aktuálny aktívny súbor.

Aktuálny kód už obsahuje:

```csharp
var escaped = Uri.EscapeDataString(packageName);
```

### Prečo je to problém

Ak zostane historický text bez vysvetlenia, implementačný agent môže hľadať neexistujúci súbor alebo opakovať už vyriešenú opravu.

### Návrh opravy

- Tento bod ponechať iba ako historický register, nie ako otvorený source-code finding.
- Nespúšťať žiadnu ďalšiu zmenu pre URL encoding bez nového konkrétneho reprodukovateľného problému.
- Aktuálne HTTP lifecycle otázky rieši `FINDING-13`.

### Akceptačné kritériá

- Dokument neodkazuje na `docs/self-review.md` ako na aktuálny súbor.
- URL encoding je označený ako uzavretý.

---

## DOC-03 — `design_handoff/README.md` opisuje `yay/paru` picker, ktorý aktuálne neexistuje

**Závažnosť:** MEDIUM
**Typ:** design/implementation drift

### Lokalizácia

```text
design_handoff/README.md:4
 design_handoff/README.md:18
 design_handoff/README.md:35
```

### Aktuálny stav

Design handoff opisuje:

- `yay/paru` segmented picker,
- detekciu yay/paru,
- prepínanie medzi oboma engine.

Aktuálny `SettingsViewModel.BuildEngineOptions()` však ponúka iba `PackageManagerEngine.Yay`.

### Prečo je to problém

Implementačný agent môže podľa design handoffu znovu vytvoriť UI voľbu `paru` bez backendu, čo by obnovilo pôvodný YAGNI/least-surprise problém.

### Návrh opravy

Vyberte jednu cestu:

1. Implementovať `ParuPackageBackend`, factory selection, testy a UI.
2. Alebo označiť `paru` v design handoff ako future feature a aktualizovať screenshot/design scope.

Do času skutočnej implementácie nesmie UI ponúkať `paru` ako použiteľnú voľbu.

### Akceptačné kritériá

- Design, requirements, README a runtime UI majú rovnaký engine scope.

---

## DOC-04 — README a implementation status nadhodnocujú Real backend completeness

**Závažnosť:** MEDIUM
**Typ:** documentation drift

### Lokalizácia

```text
README.md:29-39
README.md:57-80
docs/implementation-status.md:16-22
docs/implementation-status.md:68-73
```

### Aktuálny stav

README a status prezentujú ako hotové najmä:

- real yay mode,
- package statistics,
- package detail flow,
- backend detection.

Pôvodné review potvrdilo problémy FINDING-01 až FINDING-05. Claude ich v working tree implementačne riešil; aktuálne residual riziká sú zapísané ako `NEW-04` a `NEW-05` a real-Arch runtime stále nebol overený.

- timezone/DST pravidlá ešte nie sú overené cez skutočný transition date (`NEW-04`),
- `pacman -Qm` sa stále mapuje priamo na AUR namiesto Foreign/Unknown (`NEW-05`),
- real-Arch behavior nebol overený na skutočnom Arch/CachyOS hoste.

### Návrh opravy

Kým sa findings neopraví:

- ponechať oddelenie Demo verification vs. real-Arch verification,
- vyriešiť `NEW-04` a `NEW-05`,
- uviesť presný stav gated Arch tests,
- aktualizovať README až po skutočnom runtime overení.

Po oprave findings aktualizovať status podľa reálneho test outputu.

### Akceptačné kritériá

- Dokumentácia nerozlišuje „kód existuje“ od „feature funguje v reálnom hoste“.
- Každá feature má uvedené test level: unit, integration, headless E2E, GUI manual alebo real Arch.

---

## DOC-05 — CI workflow existuje, ale security a reproducibility problémy zostávajú

**Závažnosť:** LOW/MEDIUM
**Typ:** release/process gap / superseded documentation

### Lokalizácia

```text
README.md:3-5
.github/workflows/ci.yml
.github/workflows/release.yml
```

### Aktuálny stav

Claude pridal do working tree:

- `.github/workflows/ci.yml`,
- `.github/workflows/release.yml`.

Pôvodné tvrdenie, že `.github/workflows` neexistuje, je preto zastarané. CI už obsahuje build, test a vulnerability scan kroky.

**Update (2026-08-06): `NEW-01` a `NEW-08` sú opravené.** `.github/workflows/ci.yml` má teraz dva joby: `pr-checks` beží na GitHub-hosted `ubuntu-24.04` a spúšťa sa len na `pull_request` (build Debug+Release, unit testy, headless E2E, vulnerability scan — bez integration testov, keďže tie robia real network calls a nemajú čo bežať proti neoverenému PR kódu); `full-suite` beží na self-hosted CachyOS runneri a spúšťa sa len na `push` alebo `workflow_dispatch` (nikdy na `pull_request`). Debug/Release output paths sú teraz oddelené cez `$(Configuration)`-scoped `OutputPath`/`IntermediateOutputPath` (`Directory.Build.props` + každý `.csproj`), takže `--no-build` v CI vždy testuje presne tú konfiguráciu, ktorú step hovorí, že testuje.

Zostávajúce (nižšia priorita, mimo rozsahu tejto opravy):

- GitHub Actions v `ci.yml` sú pinnuté na verzia tag (`@v4`), nie na commit SHA — toto prostredie nemalo sieťový prístup na bezpečné overenie presných SHA hodnôt počas tejto opravy, takže pinning na SHA zostáva zdokumentovaný ako budúci krok namiesto hádania SHA hodnoty (čo by bolo horšie ako žiadny pin).
- README Tests badge je statický a nie je priamo naviazaný na GitHub Actions status.
- `release.yml` je `workflow_dispatch`-only (vyžaduje niekoho s write prístupom k repu, čo je samo o sebe approval gate), ale nemá samostatný GitHub Environment/reviewer gate navyše.

### Návrh opravy

- ~~Odstrániť staré tvrdenie „CI neexistuje“.~~ Hotové.
- ~~Opraviť bezpečnostný model podľa `NEW-01`.~~ Hotové.
- ~~Opraviť configuration isolation podľa `NEW-08`.~~ Hotové.
- README badge nahradiť skutočným GitHub Actions status badge alebo ho označiť ako manuálne aktualizovaný — zostáva otvorené.
- Pinnovať Actions na commit SHA, keď bude dostupné overenie cez sieť — zostáva otvorené.
- Release workflow chrániť explicitným approval/environment gate navyše k `workflow_dispatch` — zostáva otvorené, nízka priorita.

### Akceptačné kritériá

- ~~Review dokument opisuje aktuálny stav CI.~~ Hotové (tento update).
- ~~PR workflow nespúšťa nedôveryhodný kód na osobnom persistentnom Arch hoste.~~ Hotové — `pull_request` beží výhradne na GitHub-hosted runneri.
- ~~CI testuje presnú konfiguráciu, ktorú uvádza v názve kroku.~~ Hotové — oddelené output paths.
- Release/destructive workflow vyžaduje explicitné schválenie — čiastočne (manuálny `workflow_dispatch`), formálny approval gate zostáva otvorený.

---

# 2A. Residual nálezy po Claude fixoch

Nasledujúce nálezy vznikli pri druhom review po implementácii fixov. Nejde o opakovanie už opravených bodov. Pri každom je uvedené, čo bolo opravené a čo ešte zostáva.

---

## NEW-01 — Self-hosted CachyOS runner spúšťa neoverený PR kód na persistentnom hoste

**Stav: OPRAVENÉ (2026-08-06).** `pull_request` teraz beží výhradne v novom `pr-checks` jobe na GitHub-hosted `ubuntu-24.04`; self-hosted CachyOS runner (`full-suite` job) je gated na `if: github.event_name == 'push' || github.event_name == 'workflow_dispatch'`, takže `pull_request` ho nikdy nemôže spustiť. Pozri `DOC-05` pre presný popis a zostávajúce (nízka priorita) položky.

**Závažnosť:** HIGH; pri verejnom repository potenciálne CRITICAL
**Typ:** cybersecurity / CI isolation
**Oblasť:** GitHub Actions, untrusted pull requests, self-hosted runner

### Lokalizácia

```text
.github/workflows/ci.yml:3-7
.github/workflows/ci.yml:15-20
.github/workflows/ci.yml:23-54
.github/workflows/release.yml:11-20
```

### Aktuálny stav

CI workflow reaguje na pull requesty:

```yaml
on:
  pull_request:
    branches: [main, develop]
```

a používa:

```yaml
runs-on: [self-hosted, cachyos]
```

Následne checkoutne a spustí kód z PR:

```yaml
- uses: actions/checkout@v4
- run: dotnet build ...
- run: dotnet run ...
```

Pri CI integration kroku sa navyše nastavuje:

```yaml
YAY_SEE_SHARP_RUN_ARCH_INTEGRATION_TESTS: "1"
```

### Prečo je to problém

PR môže meniť C# testy, `.csproj`, MSBuild targety, build properties, shell skripty alebo test commands. Tento kód sa vykoná na trvalom self-hosted CachyOS hoste, kde môže:

- spúšťať ľubovoľné príkazy,
- čítať lokálne súbory a credentials runner používateľa,
- meniť stav systému alebo používateľského profilu,
- používať `sudo`, `pacman` alebo package operations,
- spúšťať deštruktívne Arch testy,
- zanechať kompromitovaný stav pre ďalšie workflow behy.

GitHub security guidance odporúča nepúšťať nedôveryhodný PR kód na persistentnom self-hosted runneri bez silnej izolácie.

### Návrh opravy

Odporúčaná stratégia:

1. `pull_request` CI spúšťať na GitHub-hosted `ubuntu-24.04` runneri.
2. Na PR workflow spúšťať iba bezpečné build/unit/Demo/headless E2E testy.
3. Self-hosted CachyOS runner rezervovať pre:
   - manuálny `workflow_dispatch`,
   - trusted push do `main`,
   - alebo explicitne schválené interné PR.
4. Real Arch destructive tests presunúť do samostatného manuálneho workflow.
5. Self-hosted runner nesmie obsahovať osobné SSH keys, broad credentials ani široké `NOPASSWD` sudo pravidlá.
6. Actions pinovať na commit SHA namiesto voľného `@v4`, ak má workflow slúžiť ako security boundary.
7. Ak musí zostať self-hosted runner, použiť disposable VM/image a reset po každom jobe.

### Povinné testy/verifikácia

- Fork PR nesmie dostať job na CachyOS self-hosted runneri.
- Bežný PR job musí prejsť na GitHub-hosted runneri bez destructive gate.
- Manuálny Arch workflow musí mať explicitný approval/dispatch.
- Overiť, že žiadny PR workflow nedostane osobné credentials runnera.

### Akceptačné kritériá

Nedôveryhodný PR nesmie spúšťať ľubovoľný PR kód na persistentnom osobnom CachyOS hoste.

---

## NEW-02 — Race pri mazaní activation socketu v `Dispose()` single-instance služby

**Závažnosť:** MEDIUM
**Typ:** concurrency/lifecycle
**Oblasť:** single instance IPC

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Platform/FileLockSingleInstanceService.cs:78-117
```

### Aktuálny stav

`Dispose()` najprv uvoľní lock:

```csharp
_lockStream?.Dispose();
_lockStream = null;
```

a až potom maže socket:

```csharp
File.Delete(_socketPath);
```

### Prečo je to problém

Medzi týmito operáciami môže nová instancia:

1. získať uvoľnený lock,
2. vytvoriť nový listener,
3. bindnúť nový `activate.sock`,
4. starý proces následne socket vymaže.

Nová živá instancia potom zostane bez funkčného activation endpointu.

### Návrh opravy

- Socket cleanup vykonať pred uvoľnením locku.
- Po zastavení listenera odstrániť socket a až potom zavrieť lock stream.
- Ešte bezpečnejšie je starý socket po uvoľnení locku nemažať vôbec, ak ďalšia instancia môže vytvoriť nový endpoint.
- Zvážiť atomic startup protocol, v ktorom lock vlastní celý transition od listener cleanup po nový bind.

### Povinné testy

- paralelný dispose/acquire loop,
- druhá instancia štartujúca počas dispose prvej,
- activation request počas lifecycle transition,
- nový listener musí zostať dostupný po získaní locku.

### Akceptačné kritériá

Starý proces nesmie po uvoľnení locku zmazať socket novej živej instancie.

---

## NEW-03 — `TryAcquire()` môže držať lock alebo vyhodiť výnimku, ak zlyhá listener

**Závažnosť:** MEDIUM
**Typ:** error handling/lifecycle
**Oblasť:** single-instance startup

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Platform/FileLockSingleInstanceService.cs:35-56
source/yay_see_sharp.infrastructure/Platform/FileLockSingleInstanceService.cs:120-137
```

### Aktuálny stav

Flow je:

```csharp
_lockStream = new FileStream(...);
StartActivationListener();
return true;
```

`StartActivationListener()` môže zlyhať pri:

- permission error,
- nepodporovanom Unix socket environment,
- príliš dlhej socket path,
- `Bind()` failure,
- chybe pri stale socket cleanup.

Lock stream je vtedy už získaný, ale cleanup nie je garantovaný.

### Prečo je to problém

Aplikácia môže pri štarte spadnúť na neobslúženej exception alebo držať lock bez funkčného listenera. Ďalší štart potom môže vyzerať ako „aplikácia už beží“, hoci prvá instancia sa nespustila správne.

### Návrh opravy

Urobiť acquire/listener startup transakčný:

```text
acquire lock
start listener
if listener fails:
    dispose listener resources
    dispose lock stream
    return explicit failure
```

Rozlíšiť:

- lock held by another instance,
- runtime directory permission failure,
- IPC listener failure.

Bootstrap má dostať user-friendly error alebo bezpečný unavailable stav, nie nepozorovanú výnimku.

### Povinné testy

- Bind failure po úspešnom lock acquire,
- permission denied,
- socket path failure,
- lock sa po listener failure uvoľní,
- ďalšia instancia môže po failure korektne štartovať.

### Akceptačné kritériá

`TryAcquire()` nikdy nevráti úspech bez funkčného listenera a pri čiastočnom failure nezanechá lock.

---

## NEW-04 — Scheduler používa aktuálny offset namiesto timezone rules cez DST prechod

**Závažnosť:** MEDIUM
**Typ:** time-zone correctness
**Oblasť:** update scheduler

### Lokalizácia

```text
source/yay_see_sharp.domain/Scheduling/UpdateScheduleCalculator.cs:5-12
source/yay_see_sharp.infrastructure/Scheduling/UpdateScheduler.cs:129-139
source/yay_see_sharp.infrastructure/Platform/SystemClock.cs:9
```

### Aktuálny stav

Scheduler už správne používa `LocalNow`, čo opravuje pôvodný UTC bug. Kalkulátor však tvorí target takto:

```csharp
new DateTimeOffset(
    now.Year,
    now.Month,
    now.Day,
    scheduledTime.Hour,
    scheduledTime.Minute,
    scheduledTime.Second,
    now.Offset)
```

a nasledujúci deň počíta cez:

```csharp
candidate.AddDays(1)
```

To zachováva aktuálny offset a nevyhodnocuje timezone transition rules.

### Prečo je to problém

Pred prechodom CET → CEST alebo CEST → CET sa offset nasledujúceho dňa môže líšiť. Nastavenie „10:00 local time“ môže byť potom interným targetom posunuté o hodinu.

Testy s pevnými offsetmi `+1` a `+2` overujú iba local offset model, nie skutočný prechod v `Europe/Bratislava`.

### Návrh opravy

Použiť timezone-aware výpočet:

- `TimeZoneInfo.Local`,
- lokálny `DateTime` bez starého offsetu,
- `TimeZoneInfo.ConvertTimeToUtc`,
- explicitne definované správanie pre neexistujúci čas pri spring-forward,
- explicitne definované správanie pre duplicitný čas pri fall-back.

### Povinné testy

- deň pred jarným DST prechodom,
- deň pred jesenným DST prechodom,
- `Europe/Bratislava`,
- schedule v čase `02:30` počas spring-forward,
- schedule počas duplicated hour pri fall-back,
- overenie, že používateľský wall-clock čas zostane správny.

### Akceptačné kritériá

Scheduler musí interpretovať `TimeOnly` ako wall-clock time používateľa aj cez skutočné DST transition dates.

---

## NEW-05 — `pacman -Qm` klasifikuje všetky foreign balíky ako AUR

**Závažnosť:** MEDIUM
**Typ:** data correctness
**Oblasť:** AUR/Official/Foreign classification

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Yay/PacmanQueryService.cs:46-53
source/yay_see_sharp.infrastructure/Yay/YayOutputParser.cs:134-138
source/yay_see_sharp.infrastructure/Yay/IPacmanQueryService.cs:10-11
```

### Aktuálny stav

Backend spúšťa:

```text
pacman -Qm
```

a každý výsledok klasifikuje ako:

```csharp
PackageSource.Aur
```

### Prečo je to problém

`pacman -Qm` znamená foreign packages — balíky, ktoré nie sú v aktuálne zapnutých repository databázach. Môže ísť o:

- AUR balík,
- manuálne stiahnutý Arch package,
- balík z externého repository,
- lokálne/firemne vytvorený balík,
- balík odstránený z repository.

Foreign package preto nie je automaticky AUR package.

Nesprávna klasifikácia ovplyvňuje:

- AUR count,
- source tagy,
- update source,
- PKGBUILD endpoint,
- používateľské očakávania pri správe balíka.

### Návrh opravy

Odporúčaná možnosť:

1. Rozšíriť `PackageSource` o `Foreign` alebo `Unknown`.
2. `pacman -Qm` mapovať na `Foreign`, nie `Aur`.
3. AUR potvrdiť samostatným AUR metadata query/API alebo spoľahlivou yay metadata cestou.
4. Ak overenie nie je možné, zobrazovať `Foreign/Unknown`.
5. AUR statistics počítať iba z potvrdených AUR balíkov.

### Povinné testy

- official/native package,
- AUR package,
- manually installed foreign package,
- external repository package,
- source tag a PKGBUILD endpoint pre každý stav,
- statistics count podľa potvrdeného source.

### Akceptačné kritériá

`AurCount` nesmie byť iba počet riadkov z `pacman -Qm`, pokiaľ je tento údaj v UI označený ako AUR.

---

## NEW-06 — Backend installer je deklarovaný ako otestovaný, ale nemá vlastné testy

**Závažnosť:** MEDIUM
**Typ:** missing test coverage / privileged flow
**Oblasť:** missing `yay` installation

### Lokalizácia

```text
source/yay_see_sharp.infrastructure/Yay/YayBackendInstaller.cs
source/yay_see_sharp.application/ViewModels/BackendInstallPromptViewModel.cs
source/yay_see_sharp.application/Views/BackendInstallPromptView.axaml
docs/implementation-status.md:50
```

### Aktuálny stav

Dokumentácia uvádza backend install flow ako:

```text
Hotové (kód + unit testy)
```

V test projekte však nie sú samostatné testy pre:

- `YayBackendInstaller`,
- `BackendInstallPromptViewModel`,
- CachyOS `pacman` install path,
- plain Arch `git clone` + `makepkg` path,
- cancellation/failure/exception cleanup,
- command preview.

### Prečo je to problém

Ide o privileged flow, ktorý môže:

- spúšťať `sudo`,
- klonovať AUR repository,
- spúšťať `makepkg`,
- meniť package database,
- vytvárať a mazať temporary build directories.

Existujúce backend tests nepokrývajú tento nový komponent.

Ďalší problém je `BackendInstallPromptViewModel.ConfirmAsync()`, ktorý nemá vlastný `try/catch/finally` okolo async install streamu. Exception z installeru môže faultnúť command task a ponechať overlay/operation v nekonzistentnom stave. Počas bežiacej inštalácie je `CloseCommand` disabled, takže používateľ nemá explicitný cancel flow.

### Návrh opravy

- Pridať mockovaný `IBackendInstaller`.
- Testovať CachyOS a plain Arch command paths.
- Testovať success, failure, cancellation aj exception.
- Testovať presné `CommandRequest` arguments a `WorkingDirectory`.
- Pridať explicitný Cancel command počas operácie.
- V `ConfirmAsync()` použiť `try/catch/finally` a user-facing error state.
- Po failure musí byť možné prompt zavrieť a operation zopakovať.
- Zabezpečiť cleanup temporary build directory aj pri exception/cancel.

### Akceptačné kritériá

Backend install flow má vlastné testy pre všetky paths a pri žiadnom error path nezostane zamrznutý modal ani privilegovaný child process.

---

## NEW-07 — `IEngineDetector` zostal v Infrastructure namespace a Application naň priamo závisí

**Závažnosť:** MEDIUM
**Typ:** architecture/DIP boundary
**Oblasť:** composition root, assembly boundaries

### Lokalizácia

```text
source/yay_see_sharp.application/ViewModels/SettingsViewModel.cs:6,13,36
source/yay_see_sharp.infrastructure/Platform/EngineDetector.cs:5-10
```

### Aktuálny stav

`SettingsViewModel` používa `IEngineDetector` z:

```csharp
using yay_see_sharp.infrastructure.Platform;
```

Reflection architecture test kontroluje concrete Infrastructure classes, ale nie interfaces z Infrastructure namespace. Preto test prejde, hoci Application assembly stále pozná Infrastructure namespace.

### Prečo je to problém

Deklarovaná architektúra hovorí, že ViewModels majú závisieť od domain/application abstractions a Infrastructure ich má implementovať. `IEngineDetector` je contract, preto patrí k abstractions podobne ako:

- `ISettingsStore`,
- `IFolderBrowserService`,
- `IPkgbuildService`,
- `IPrivilegeService`.

### Návrh opravy

- Presunúť `IEngineDetector` do `source/yay_see_sharp.domain/Abstractions`.
- `EngineDetector` implementáciu ponechať v Infrastructure.
- `SettingsViewModel` importovať iba domain abstractions.
- Architecture test rozšíriť aj o interface types z `yay_see_sharp.infrastructure` namespace a o zakázané Infrastructure `using` directives vo ViewModels.
- Design-time factory môže zostať explicitnou výnimkou, ale musí byť zdokumentovaná a testovaná ako non-production path.

### Akceptačné kritériá

Application/ViewModels pozná iba contract, nie namespace konkrétnej Infrastructure vrstvy.

---

## NEW-08 — CI Debug testy môžu po Release builde používať prepisované artefakty

**Stav: OPRAVENÉ (2026-08-06).** `Directory.Build.props` teraz nastavuje `OutputPath=$(MSBuildThisFileDirectory)output\bin\$(Configuration)\` centrálne pre všetky projekty; každý `.csproj`'s `IntermediateOutputPath` má tiež pridaný `$(Configuration)` segment. Debug a Release teraz buildujú do fyzicky oddelených priečinkov (`output/bin/Debug/`, `output/bin/Release/`), overené manuálne (build Debug → build Release → `dotnet run --no-build` proti Debug testom stále beží proti Debug binárkam).

**Závažnosť:** LOW/MEDIUM
**Typ:** CI correctness/reproducibility
**Oblasť:** build output isolation

### Lokalizácia

```text
.github/workflows/ci.yml:29-45
source/*/*.csproj — spoločný OutputPath `output/bin`
```

### Aktuálny stav

CI workflow najprv buildne:

```yaml
dotnet build ... --configuration Debug
dotnet build ... --configuration Release
```

a potom spúšťa:

```yaml
dotnet run ... --configuration Debug --no-build
```

Projekty používajú spoločný `output/bin` bez configuration-specific output directory. Release build preto môže prepísať artefakty, ktoré následne používa Debug `--no-build` test run.

### Prečo je to problém

CI môže deklarovať, že spustila Debug testy, ale test process môže načítať posledné skompilované Release assembly. Výsledok je menej reprodukovateľný a môže skrývať configuration-specific regresiu.

### Návrh opravy

Vybrať jednu konzistentnú možnosť:

1. Oddeliť output paths podľa `$(Configuration)`.
2. Spustiť Debug tests pred Release buildom a explicitne overiť, že assembly paths sú Debug.
3. Buildnúť a testovať Debug/Release v oddelených joboch.
4. Nepoužívať `--no-build`, ak output path nie je configuration-specific.

### Povinné testy/verifikácia

- overiť path a timestamp načítanej test assembly,
- Debug build + Debug tests,
- Release build + Release smoke/test,
- CI clean checkout bez zdieľaných stale artefacts.

### Akceptačné kritériá

Workflow testuje presne tú konfiguráciu, ktorú uvádza v názve kroku.

---

## NEW-09 — `DOC-05` v pôvodnom review už nezodpovedá aktuálnemu working tree

**Závažnosť:** LOW/MEDIUM
**Typ:** documentation drift

### Lokalizácia

```text
docs/code-review-findings.md:1114-1159
.github/workflows/ci.yml
.github/workflows/release.yml
```

### Aktuálny stav

Pôvodný `DOC-05` tvrdí, že v repozitári neexistuje `.github/workflows` CI pipeline. Claude však pridal:

- `.github/workflows/ci.yml`,
- `.github/workflows/release.yml`.

Pôvodný nález je preto ako „CI neexistuje“ zastaraný. Jeho bezpečnostná podstata však zostáva otvorená a je teraz pokrytá `NEW-01`. Navyše README badge je stále statický a CI workflow badge automaticky neaktualizuje.

### Návrh opravy

- `DOC-05` preformulovať na: „CI existuje, ale treba opraviť self-hosted PR security a overiť reproducibility/configuration isolation“.
- Duplicitné otvorenie „CI neexistuje“ odstrániť.
- Odkázať na `NEW-01` a `NEW-08`.
- README badge nahradiť skutočným GitHub Actions status badge alebo ho jasne označiť ako manuálne aktualizovaný.

### Akceptačné kritériá

Review dokument neobsahuje tvrdenie, že CI workflow neexistuje, keď už workflow v working tree existuje.

---

# 2B. UI/UX nálezy z používateľského testovania (UI-01 až UI-23)

Register nálezov z manuálneho testovania na Ubuntu (približne 23 nálezov) a CachyOS (niekoľko nálezov). Presný pôvodný zoznam od používateľa nie je v repozitári ako samostatný súbor; tento register je rekonštruovaný z `docs/implementation-status.md` (časť „Zmeny z tejto session") a z `UI-xx` komentárov priamo v kóde. Stav = **implementované a overené v kóde** pri treťom review (2026-08-06), build Debug+Release 0 warningov/0 chýb, testy 268 passed / 3 skipped.

| ID | Nález (rekonštruovaný) | Stav | Lokalizácia |
|---|---|---|---|
| UI-01 | Chýbajúce/nevýrazné vizuálne potvrdenie akcií — doplnené toast notifikácie pre install/uninstall/update/error | ✅ implementované | `ToastService`, `ToastViewModel`, `MainWindow.axaml` (toast stack), `CompositeNotificationService` |
| UI-02 | Minimize do tray nejednotné s close-to-tray | ✅ implementované | `App.axaml.cs` — `WindowStateProperty` observer pri `HideToTray` |
| UI-03 | Tray ikona nebola viditeľná pred prvým hide | ✅ implementované | `App.axaml.cs` — `_trayService.Show()` pri štarte (UI-21) |
| UI-04 | Po operácii treba ručne refreshovať dáta | ✅ implementované | `InstalledPackagesViewModel` auto-refresh po `Completed` (UI-04/UI-11/UI-13) |
| UI-05 | Kliknutie na update riadok nemá navigáciu do detailu | ✅ implementované | `DashboardViewModel.PackageActivated` → `MainWindowViewModel` → `InstalledPackagesViewModel.SelectByName`; `UpdateItemViewModel.SelectCommand`; `DashboardView.axaml` |
| UI-06 | Toast auto-dismiss | ✅ implementované | `ToastService` — 30s auto-dismiss |
| UI-07 | Search vyžaduje tlačidlo, nie live výsledky | ✅ implementované | `SearchViewModel` — `Throttle(300ms)` + `.Switch()` live search; `SearchView.axaml` bez tlačidla |
| UI-08 | Prázdne search pole = prázdna obrazovka | ✅ implementované | `SearchViewModel.LoadRecommendedAsync` — curated recommended packages |
| UI-09 | Search riadok nie je klikateľný do detailu | ✅ implementované | `SearchViewModel.SelectPackageCommand`; `SearchView.axaml` — riadok ako `Button Classes="row"` |
| UI-10 | Search detail v samostatnom okne namiesto split panela | ✅ implementované | `SearchView.axaml` — dvojstĺpcový grid (výsledky + detail) |
| UI-11 | Refresh tlačidlo na Dashboarde zbytočné | ✅ implementované | `DashboardView.axaml` — Refresh tlačidlo odstránené |
| UI-12 | Refresh tlačidlo na Installed zbytočné | ✅ implementované | `InstalledPackagesView.axaml` — auto-refresh po operácii |
| UI-13 | Installed list nemá source filter | ✅ implementované | `InstalledPackagesViewModel` — `SourceOptions` All/Official/AUR + `FilteredPackages` |
| UI-14 | Settings v hlavnom sidebar liste | ✅ implementované | `MainWindow.axaml` — Settings tlačidlo pinned dole; `MainNavigationItems`/`SettingsNavigationItem`/`IsSettingsSelected` |
| UI-15 | „Saved" indikátor v Settings nenápadný | ✅ implementované | toast cez `ToastService` v `SettingsViewModel` |
| UI-16 | PKGBUILD modal malé Close | ✅ implementované | `PkgbuildView.axaml` — veľké Close tlačidlo |
| UI-17 | Settings nevie, v akom móde beží | ✅ implementované | `SettingsViewModel.SetBackendMode(backend.Info.Mode)` v `AppBootstrapper` |
| UI-18 | Engine picker (yay) v simulovanom móde nič nerobí bez vysvetlenia | ✅ implementované | `SettingsView.axaml.cs OnEngineOptionsPressed` → `NotifyIfSimulated`; localization keys `Settings.SimulatedMode*` |
| UI-19 | Popis update schedule tvrdí „Every 6 hours" aj keď je denný | ✅ implementované | `LocalizationResources` — `Settings.AutoUpdate.HintDaily` = „Daily check at {0}" |
| UI-20 | Fonty príliš malé | ✅ implementované | FontSize zdvojnásobené naprieč Views (22–38) |
| UI-21 | Tray ikona od štartu | ✅ implementované | `App.axaml.cs` (rovnaká zmena ako UI-03) |
| UI-22 | Toast overlay v app | ✅ implementované | `MainWindow.axaml` toast `ItemsControl` + `NotificationLevelBrushConverter` + `Color.Success` |
| UI-23 | Chýba `.desktop` súbor / packaging pre distribúciu | ✅ implementované | `packaging/yay-see-sharp.desktop`, `packaging/icons/yay-see-sharp.png`, `docs/aur-packaging-guide.md` |

**Poznámka k overeniu:** UI-01 až UI-23 sú overené na úrovni kódu (build, unit testy, headless E2E). Vizuálne a interaktívne overenie na reálnom Ubuntu/CachyOS desktope (X11/Wayland, tray, D-Bus notifikácie, reálny sudo prompt) je stále **otvorené** a patrí do manuálnej verifikácie používateľom.

---

# 2C. Nálezy z tretieho review (po implementácii UI/UX + NEW fixov)

Tieto nálezy vznikli pri treťom prechode review (2026-08-06) po tom, čo agent oznámil kompletnú implementáciu. Build Debug aj Release: 0 warnings / 0 errors. Testy: 268 passed / 0 failed / 3 skipped. Všetky nálezy nižšie sú **neblokujúce** — nejde o regresie, ale o hardening a konzistentnosť.

---

## NEW-10 — Odporúčané balíky v Search spúšťajú 16 paralelných `yay` procesov

**Závažnosť:** MEDIUM (Real mode performance)
**Typ:** performance / process explosion
**Oblasť:** SearchViewModel, live search, Real mode

### Lokalizácia

```text
source/yay_see_sharp.application/ViewModels/SearchViewModel.cs:14-22
source/yay_see_sharp.application/ViewModels/SearchViewModel.cs:185-220 (LoadRecommendedAsync)
```

### Aktuálny stav

`SearchViewModel` sa konštruuje **eager** v `AppBootstrapper` (rovnako ako ostatné child screens), takže `LoadRecommendedAsync().FireAndForget()` beží **pri štarte aplikácie**. Pre každý zo 16 odporúčaných balíkov spúšťa samostatné:

```csharp
_backend.SearchAsync(name, SourceFilter)
```

v `Task.WhenAll` — teda v Real mode **16 súbežných `yay -Ss` procesov** naraz (každý refreshuje sync-db + AUR RPC). To isté sa opakuje pri každom vyprázdnení search poľa.

### Prečo je to problém

- 16 súbežných yay procesov pri štarte a pri každom prázdnom query = zbytočná záťaž, pomalý štart na reálnom Arch hoste, burst sieťovej a databázovej aktivity.
- Demo mode to nemá (in-memory), takže sa to neprejaví v sandbox testoch — objaví sa až na CachyOS.

### Návrh opravy

1. Spojiť do **jedného** `yay -Ss -- <16 mien>` batch query a výsledky namapovať podľa názvu (parsovať cez `YayOutputParser.ParseSearch` a filtrovať presné zhody).
2. Alebo načítať odporúčané balíky **lazy** — až keď používateľ prvýkrát otvorí Search screen (nie v konštruktore pri štarte).
3. Alebo cacheovať výsledky na čas (napr. 5 minút), aby opakované prázdne query nevolali backend.

### Akceptačné kritériá

- Štart aplikácie v Real mode nespúšťa 16 súbežných yay procesov.
- Prázdne search pole nespúšťa batch procesov pri každom vyprázdnení.

---

## NEW-11 — `ToastService` auto-dismiss nemá cancellation token

**Závažnosť:** LOW
**Typ:** lifecycle/hardening
**Oblasť:** ToastService

### Lokalizácia

```text
source/yay_see_sharp.application/Platform/ToastService.cs:40-42
```

### Aktuálny stav

```csharp
Task.Delay(AutoDismissAfter).ContinueWith(
    _ => Dispatcher.UIThread.Post(() => Toasts.Remove(toast)),
    TaskScheduler.Default);
```

Bez `CancellationToken`: pri shutdown aplikácie môže pending `ContinueWith` strieľať `Dispatcher.UIThread.Post` po zničení UI kolekcie. V praxi je to neškodné (Post po shutdown sa zahodí), ale je to lifecycle zrada — `Toasts` patrí UI a auto-dismiss by mal zaniknúť s app lifetime.

### Návrh opravy

- Pridať `CancellationToken` (app lifetime CTS) do `Task.Delay` a `ContinueWith` so `TaskContinuationOptions.NotOnCanceled`.
- Prípadne auto-dismiss zrušiť pri manuálnom dismiss toastu.

### Akceptačné kritériá

- Po shutdown nebeží žiadny pending auto-dismiss timer.
- Manuálne dismissnutý toast sa neskúša odstraňovať znova.

---

## NEW-12 — Test counts v dokumentácii sú neaktuálne (259 vs. realita 268)

**Závažnosť:** LOW/MEDIUM
**Typ:** documentation drift

### Lokalizácia

```text
README.md:5 (Tests badge — 259 passed)
docs/implementation-status.md:21 (262 total / 259 passed)
```

### Aktuálny stav

Pri treťom review reálne bežalo:

```text
domain 7 / infrastructure 146 / application 94 / e2e 10 / integration 11 (+3 skipped)
spolu 268 passed, 0 failed, 3 skipped (271 total)
```

README a implementation-status uvádzajú 259 passed / 262 total — rozdiel vznikol pridaním nových testov (`YayBackendInstallerTests`, `BackendInstallPromptViewModelTests`, `AuthPromptE2ETests`, rozšírené `UpdateSchedulerTests`/`FileLockSingleInstanceServiceTests`).

### Návrh opravy

- Aktualizovať README badge a implementation-status na aktuálne čísla (268 passed / 3 skipped) — najlepšie generovať z CI namiesto statického badge.
- Pridať do `# 6. Povinný výstup implementačného agenta` požiadavku uviesť test counts z reálneho posledného behu, nie odhad.

### Akceptačné kritériá

- README/implementation-status čísla zodpovedajú poslednému reálnemu test runu.

---

## NEW-13 — `SelectByName` ticho zlyhá, ak balík ešte nie je v Installed liste

**Závažnosť:** LOW
**Typ:** UX edge case
**Oblasť:** InstalledPackagesViewModel / Dashboard navigácia

### Lokalizácia

```text
source/yay_see_sharp.application/ViewModels/InstalledPackagesViewModel.cs:192-213 (SelectByName)
```

### Aktuálny stav

`SelectByName(name)` hľadá balík v `Packages` a ak tam nie je, nič neurobí — navigácia z Dashboard update listu do Installed prebehne, ale detail sa nezobrazí (tichý no-op).

### Prečo je to problém

Update balík by normálne mal byť nainštalovaný, ale ak je `Packages` ešte nenačítaný (refresh beží) alebo bol balík medzičasom odstránený, používateľ skončí na prázdnej obrazovke bez vysvetlenia.

### Návrh opravy

- Ak balík chýba: spustiť `RefreshAsync()` a po dokončení sa pokúsiť vybrať znova.
- Alebo zobraziť explicitný stav „balík sa nenašiel / ešte sa načítava".

### Akceptačné kritériá

- Navigácia z Dashboard update riadku vždy skončí buď na detaile balíka, alebo na zrozumiteľnej správe.

---

## NEW-14 — `MainWindowViewModel` má optional `ToastService` s hidden `new`

**Závažnosť:** LOW
**Typ:** DIP konzistencia (FINDING-08/NEW-07 pattern)

### Lokalizácia

```text
source/yay_see_sharp.application/ViewModels/MainWindowViewModel.cs:31-43
```

### Aktuálny stav

```csharp
ToastService? toastService = null
...
Toasts = (toastService ?? new ToastService()).Toasts;
```

ViewModels už majú pravidlo „žiadne hidden `new Concrete()`" (FINDING-08/NEW-07), ale `ToastService` je application-vrstva trieda, takže ho `ArchitectureTests` (ktoré hľadajú iba `infrastructure` typy) nechytia.

### Návrh opravy

- Zmeniť na povinný parameter `ToastService toastService` (všetci produkční volajú cez `AppBootstrapper`; testy už ho vedia odovzdať).
- Prípadne definovať interface `IToastService` v domain/abstractions a `ToastService` ho implementovať — konzistentné s `INotificationService`.

### Akceptačné kritériá

- ViewModel neobsahuje žiadny `?? new Concrete()` fallback pre UI/domain služby.
- `ArchitectureTests` pokrýva aj application-vrstva concrete services (nielen infrastructure).

---

# 3. Už opravené staršie nálezy — nerevertovať

Nasledujúce body boli v aktuálnom working tree overené ako opravené. Implementačný agent ich nemá znovu implementovať ani označovať ako otvorené:

1. **`removeOrphans` flag v Yay uninstall:**
   - `true` používa `-Rns`,
   - `false` používa `-Rn`,
   - command display a skutočné arguments sú zosúladené.
   - Lokalizácia: `YayPackageBackend.Uninstall.cs:23-40`.

2. **`RemoveOrphansByDefault` propagation:**
   - `PackageDetailsViewModel` používa `IUninstallPolicy.RemoveOrphansByDefault`.
   - Lokalizácia: `PackageDetailsViewModel.cs:178-203`.

3. **Sudo privilege boundary:**
   - existuje `IPrivilegeService`, `SudoPrivilegeService` a `ProcessSudoInvoker`,
   - heslo nejde do argv, settings ani logov,
   - package install/uninstall/update volajú elevation flow,
   - fail-closed behavior existuje bez prompt callbacku.
   - Reálne interaktívne GUI sudo však stále nebolo manuálne overené.

4. **Update scheduler exists:**
   - existuje background loop, cancellation, run lock a scheduler tests.
   - UTC/local-time problém z FINDING-05 bol opravený; residual DST transition problém je `NEW-04`.

5. **Desktop notification abstraction exists:**
   - existuje `INotificationService`, `NotifySendNotificationService`, `SettingsAwareNotificationService` a no-op fallback.
   - Reálne D-Bus/desktop notification však nebolo manuálne overené.

6. **Filesystem/HTTP I/O abstractions:**
   - `IFolderBrowserService` a `IPkgbuildService` existujú,
   - ViewModels už priamo nepoužívajú `Directory.*` ani `HttpClient` request API.
   - PKGBUILD lifecycle/cancellation z FINDING-13 bol opravený a otestovaný.

7. **Fire-and-forget helper and operation cleanup:**
   - `Task.FireAndForget()` existuje,
   - hlavné install/uninstall/update flows majú `try/catch/finally`.
   - PKGBUILD fetch cancellation z FINDING-13 je opravená; nové installer lifecycle chyby sú v `NEW-06`.

8. **Demo/Yay contract tests:**
   - existujú shared contract tests nad Demo backendom a Fake Yay command runnerom.
   - Toto však nenahrádza real Arch host integration test.

9. **Relative time localization:**
   - `DashboardViewModel.FormatRelative()` používa localization keys pre EN/SK.

10. **PKGBUILD URL encoding:**
    - `PkgbuildService` používa `Uri.EscapeDataString(packageName)`.
    - Starý finding bez encodingu aj cancellation/timeout podľa FINDING-13 sú uzavreté.

11. **Avalonia.Diagnostics mismatch:**
    - `Avalonia.Diagnostics` 11.x už nie je v application projecte.
    - Nevracať ho späť iba preto, že je Debug tool.

12. **Shell command injection:**
    - v review nebol nájdený shell string execution,
    - process execution používa `UseShellExecute = false` a `ArgumentList`.
    - FINDING-11 argument/option hardening bol implementovaný; nové overenie reálnej `yay` syntaxe zostáva súčasťou real-Arch verifikácie.

---

# 4. Architektonické a bezpečnostné pravidlá pre všetky opravy

1. Views nesmú priamo volať `yay`, `paru`, `pacman`, `sudo`, shell, filesystem ani HTTP.
2. ViewModels nesmú robiť priame platformové I/O ani vytvárať konkrétne Infrastructure služby.
3. Domain abstractions nesmú závisieť od Avalonia alebo konkrétnej OS implementácie.
4. Production composition root má byť `AppBootstrapper`.
5. Backend selection musí používať factory/abstraction a musí rešpektovať skutočné prerequisites.
6. Každé UI setting musí mať runtime consumer, alebo musí byť z UI odstránené/označené ako future feature.
7. Settings persistence musí zostať za `ISettingsStore`.
8. Notifications musia zostať za `INotificationService`.
9. Privilege management musí zostať za `IPrivilegeService`.
10. Heslá nesmú byť v command arguments, logs, settings ani telemetry.
11. Nepoužívať shell string pre používateľský vstup.
12. Všetky user/package/query arguments validovať podľa správneho kontextu a oddeliť od command flags.
13. Scheduler musí byť injectovateľný, cancellation-safe a timezone-correct.
14. Destructive real Arch tests nesmú bežať automaticky na Ubuntu/unsupported hoste.
15. Neoznačovať feature ako hotovú iba preto, že existuje model, enum, interface alebo ViewModel.
16. Každý fix musí mať regression test alebo zdokumentované odôvodnenie, prečo test nie je možný.
17. Nerobiť nesúvisiaci refactor spolu s bezpečnostnou alebo funkčnou opravou bez samostatného testu.

---

# 5. Povinná verifikácia po opravách

Spúšťať z repository root:

```bash
cd /home/hp-camera-hub/workspace/yay_see_sharp

git diff --check

git status --short --branch

dotnet restore yay_see_sharp.slnx

dotnet build yay_see_sharp.slnx --configuration Debug

dotnet build yay_see_sharp.slnx --configuration Release

dotnet run --project tests/yay_see_sharp.domain.Tests/yay_see_sharp.domain.Tests.csproj --configuration Debug

dotnet run --project tests/yay_see_sharp.infrastructure.Tests/yay_see_sharp.infrastructure.Tests.csproj --configuration Debug

dotnet run --project tests/yay_see_sharp.application.Tests/yay_see_sharp.application.Tests.csproj --configuration Debug

dotnet run --project tests/yay_see_sharp.integration.Tests/yay_see_sharp.integration.Tests.csproj --configuration Debug

dotnet run --project tests/yay_see_sharp.e2e.Tests/yay_see_sharp.e2e.Tests.csproj --configuration Debug

dotnet list yay_see_sharp.slnx package --include-transitive

dotnet list yay_see_sharp.slnx package --vulnerable --include-transitive
```

### Povinné testovanie po jednotlivých oblastiach

#### Real backend

- Arch/CachyOS s `yay` na PATH.
- Arch/CachyOS bez `yay`.
- Search official/AUR.
- Details installed aj not installed.
- Install/uninstall/update.
- `removeOrphans=true` aj `false`.
- Statistics vrátane AUR, explicit, dependency, orphan a size fields.
- Sudo granted/cancelled/failed/expired.
- Žiadne heslo v arguments/logoch.

#### Demo mode

- Ubuntu/non-Arch detection.
- Search/filter.
- Details.
- Install/uninstall/update.
- Orphan policy true/false.
- Cancellation/failure.
- Settings persistence.

#### Scheduling

- lokálny timezone, UTC, letný/zimný čas,
- schedule change počas behu,
- disabled/enabled toggle,
- shutdown cancellation,
- duplicate check prevention.

#### Application lifecycle

- single instance,
- second launch activation,
- hide-to-tray/restore,
- explicit exit,
- tray unavailable,
- app shutdown s bežiacim schedulerom/HTTP/sudo operation.

#### GUI/manual verification

Na reálnom Linuxe s X11/Wayland:

- Dashboard startup,
- sidebar navigation,
- Search,
- installed/not-installed details,
- Demo install/uninstall,
- Settings persistence,
- theme switch,
- language switch,
- Remove Orphans behavior,
- auth prompt,
- tray restore/exit,
- desktop notification,
- PKGBUILD modal close počas fetchu.

---

# 6. Povinný výstup implementačného agenta po dokončení

Agent musí v závere uviesť:

1. zmenené súbory,
2. každý opravený finding ID,
3. každý zámerne neopravený/open finding ID s dôvodom,
4. presné build výsledky Debug aj Release,
5. presné test counts: passed/failed/skipped pre každý projekt,
6. výsledok package graph kontroly,
7. výsledok vulnerability scan,
8. výsledok real Arch testu a host prerequisites,
9. či prebehlo manuálne GUI testovanie,
10. či bol vytvorený commit — commit nesmie byť tvrdený bez reálneho `git status`/`git log` overenia.

**Cieľový release verdikt:** projekt možno označiť ako Real-Arch-release-ready až po vyriešení HIGH nálezov, po overení gated Arch flow a po zosúladení dokumentácie s aktuálnym runtime stavom.
