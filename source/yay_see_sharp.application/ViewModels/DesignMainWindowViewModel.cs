using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.infrastructure.Settings;

namespace yay_see_sharp.application.ViewModels;

/// <summary>Design-time DataContext for the XAML previewer — wires the same MainWindowViewModel against a DemoPackageBackend instead of AppBootstrapper's real infrastructure.</summary>
public sealed class DesignMainWindowViewModel : MainWindowViewModel
{
    public DesignMainWindowViewModel() : this(new DemoPackageBackend(), new LocalizationService())
    {
    }

    private DesignMainWindowViewModel(IPackageBackend backend, ILocalizationService localization)
        : base(
            backend,
            localization,
            CreateSettings(localization),
            new DashboardViewModel(backend, localization),
            new SearchViewModel(backend, localization),
            new InstalledPackagesViewModel(backend, localization))
    {
    }

    private static SettingsViewModel CreateSettings(ILocalizationService localization) =>
        new(new FileSettingsStore(), localization, AppSettings.Default);
}
