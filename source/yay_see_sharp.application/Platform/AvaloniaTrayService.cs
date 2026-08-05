using Avalonia.Controls;
using Avalonia.Platform;
using yay_see_sharp.domain.Abstractions;

namespace yay_see_sharp.application.Platform;

public sealed class AvaloniaTrayService : ITrayService
{
    private readonly ILocalizationService _localization;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _restoreItem;
    private NativeMenuItem? _exitItem;

    public AvaloniaTrayService(ILocalizationService localization)
    {
        _localization = localization;
        _localization.LanguageChanged += OnLanguageChanged;
    }

    public event EventHandler? RestoreRequested;
    public event EventHandler? ExitRequested;

    public void Show()
    {
        EnsureInitialized();
        if (_trayIcon is not null)
            _trayIcon.IsVisible = true;
    }

    public void Hide()
    {
        if (_trayIcon is not null)
            _trayIcon.IsVisible = false;
    }

    public void Dispose()
    {
        _localization.LanguageChanged -= OnLanguageChanged;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    private void EnsureInitialized()
    {
        if (_trayIcon is not null) return;

        try
        {
            _restoreItem = new NativeMenuItem(_localization.GetString("Tray.Restore"));
            _restoreItem.Click += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);

            _exitItem = new NativeMenuItem(_localization.GetString("Tray.Exit"));
            _exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

            var menu = new NativeMenu();
            menu.Items.Add(_restoreItem);
            menu.Items.Add(_exitItem);

            _trayIcon = new TrayIcon
            {
                Menu = menu,
                Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://yay_see_sharp.application/Assets/app-icon.ico"))),
                IsVisible = false,
            };
            _trayIcon.Clicked += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Tray not available on this system (e.g. GNOME without AppIndicator)
            _trayIcon = null;
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_restoreItem is not null)
            _restoreItem.Header = _localization.GetString("Tray.Restore");
        if (_exitItem is not null)
            _exitItem.Header = _localization.GetString("Tray.Exit");
    }
}
