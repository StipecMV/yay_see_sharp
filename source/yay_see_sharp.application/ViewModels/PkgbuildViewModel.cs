using System.Net.Http;
using System.Reactive;
using ReactiveUI;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Http;

namespace yay_see_sharp.application.ViewModels;

/// <summary>
/// Fetches and displays a package's raw PKGBUILD text in an in-app modal, instead of shelling
/// out to a browser. AUR packages are read from cgit's "plain" endpoint; official-repo packages
/// from the packaging GitLab mirror — the exact URL pattern lives in IPkgbuildService, not here.
/// </summary>
public sealed class PkgbuildViewModel : LocalizedViewModelBase
{
    private readonly IPkgbuildService _pkgbuildService;
    private readonly PackageSource _source;
    private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private string? _content;
    private bool _isLoading;
    private string? _errorMessage;

    public PkgbuildViewModel(string packageName, PackageSource source, IPkgbuildService? pkgbuildService, ILocalizationService localization)
        : base(localization)
    {
        PackageName = packageName;
        _source = source;
        _pkgbuildService = pkgbuildService ?? new PkgbuildService();
        CloseCommand = ReactiveCommand.Create(Close);
    }

    public string PackageName { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public string? Content
    {
        get => _content;
        private set => this.RaiseAndSetIfChanged(ref _content, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public string TitleLabel => string.Format(Localization.GetString("Pkgbuild.Title"), PackageName);

    public string CloseLabel => Localization.GetString("Pkgbuild.Close");

    public Task WaitForCloseAsync() => _completionSource.Task;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            Content = await _pkgbuildService.FetchAsync(PackageName, _source, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is { } statusCode)
        {
            ErrorMessage = string.Format(Localization.GetString("Pkgbuild.FetchFailed"), (int)statusCode);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override void RaiseLocalizedPropertiesChanged()
    {
        this.RaisePropertyChanged(nameof(TitleLabel));
        this.RaisePropertyChanged(nameof(CloseLabel));
    }

    private void Close() => _completionSource.TrySetResult();
}
