using Moq;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Demo;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class SearchViewModelTests
{
    [Test]
    public async Task Search_populates_results_from_backend()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>()) { Query = "fire" };

        await viewModel.SearchCommand.Execute();

        await Assert.That(viewModel.Results.Count).IsGreaterThan(0);
        await Assert.That(viewModel.IsBusy).IsFalse();
        await Assert.That(viewModel.ErrorMessage).IsNull();
    }

    [Test]
    public async Task Search_with_source_filter_only_returns_matching_source()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>()) { Query = "" };
        viewModel.SelectedSourceOption = viewModel.SourceOptions.Single(option => option.Value == PackageSource.Aur);

        await viewModel.SearchCommand.Execute();

        await Assert.That(viewModel.Results.Count).IsGreaterThan(0);
        foreach (var package in viewModel.Results)
        {
            await Assert.That(package.Source).IsEqualTo(PackageSource.Aur);
        }
    }

    [Test]
    public async Task Selecting_a_package_creates_details_for_it()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>()) { Query = "fire" };
        await viewModel.SearchCommand.Execute();

        viewModel.SelectedPackage = viewModel.Results[0];

        await Assert.That(viewModel.SelectedDetails).IsNotNull();
        await Assert.That(viewModel.SelectedDetails!.Summary).IsEqualTo(viewModel.Results[0]);
    }

    [Test]
    public async Task Source_options_include_all_official_and_aur_and_switch_language_live()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, localization, Mock.Of<IPkgbuildService>());

        await Assert.That(viewModel.SourceOptions.Count).IsEqualTo(3);
        await Assert.That(viewModel.SourceOptions[0].Value).IsNull();
        await Assert.That(viewModel.SourceOptions[0].Label).IsEqualTo("All");
        await Assert.That(viewModel.PlaceholderLabel).IsEqualTo("Search packages…");

        localization.SetLanguage("sk");

        await Assert.That(viewModel.SourceOptions[0].Label).IsEqualTo("Všetky");
        await Assert.That(viewModel.PlaceholderLabel).IsEqualTo("Hľadať balíky…");
    }

    [Test]
    public async Task Default_source_selection_is_all_not_empty()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>());

        await Assert.That(viewModel.SelectedSourceOption.Label).IsEqualTo("All");
        await Assert.That(viewModel.SelectedSourceOption.Value).IsNull();
        await Assert.That(viewModel.SourceFilter).IsNull();
    }

    [Test]
    public async Task Selected_source_option_survives_language_switch_by_matching_value()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, localization, Mock.Of<IPkgbuildService>());
        viewModel.SelectedSourceOption = viewModel.SourceOptions.Single(option => option.Value == PackageSource.Aur);

        localization.SetLanguage("sk");

        await Assert.That(viewModel.SelectedSourceOption.Value).IsEqualTo(PackageSource.Aur);
        await Assert.That(viewModel.SelectedSourceOption.Label).IsEqualTo("AUR");
        await Assert.That(viewModel.SourceFilter).IsEqualTo(PackageSource.Aur);
    }

    [Test]
    public async Task Typed_query_text_is_preserved_when_switching_language()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, localization, Mock.Of<IPkgbuildService>()) { Query = "firefox" };

        localization.SetLanguage("sk");

        await Assert.That(viewModel.Query).IsEqualTo("firefox");
    }

    [Test]
    public async Task Search_results_are_preserved_when_switching_language()
    {
        var localization = new LocalizationService("en");
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, localization, Mock.Of<IPkgbuildService>()) { Query = "fire" };
        await viewModel.SearchCommand.Execute();
        var resultCountBefore = viewModel.Results.Count;

        localization.SetLanguage("sk");

        await Assert.That(viewModel.Results.Count).IsEqualTo(resultCountBefore);
        await Assert.That(viewModel.Query).IsEqualTo("fire");
    }

    // --- BUGFIX-2026-08: filter switches re-search (with immediate clear), selection survives reloads ---

    [Test]
    public async Task Switching_the_source_filter_triggers_a_new_search_with_that_source()
    {
        var backend = new Mock<IPackageBackend>();
        backend.Setup(b => b.SearchAsync(It.IsAny<string>(), It.IsAny<PackageSource?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PackageSummary>());
        var viewModel = new SearchViewModel(backend.Object, new LocalizationService("en"), Mock.Of<IPkgbuildService>()) { Query = "firefox" };

        // The previous subscription watched the computed SourceFilter, which never notified —
        // filter clicks did nothing until the next keystroke. The fix watches SelectedSourceOption.
        viewModel.SelectedSourceOption = viewModel.SourceOptions.Single(option => option.Value == PackageSource.Aur);

        await WaitUntilAsync(
            () => backend.Invocations.Any(invocation =>
                invocation.Method.Name == nameof(IPackageBackend.SearchAsync) &&
                invocation.Arguments.ElementAtOrDefault(1) is PackageSource.Aur),
            TimeSpan.FromSeconds(3));

        backend.Verify(b => b.SearchAsync("firefox", PackageSource.Aur, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Test]
    public async Task Switching_the_source_filter_clears_stale_results_immediately()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>()) { Query = "fire" };
        await viewModel.SearchCommand.Execute();
        await Assert.That(viewModel.Results.Count).IsGreaterThan(0);

        viewModel.SelectedSourceOption = viewModel.SourceOptions.Single(option => option.Value == PackageSource.Aur);

        // The old filter's rows must not linger while the new search is in flight.
        await Assert.That(viewModel.Results.Count).IsEqualTo(0);
        await Assert.That(viewModel.IsBusy).IsTrue();
    }

    [Test]
    public async Task Selection_is_preserved_across_a_search_reload()
    {
        var backend = new DemoPackageBackend();
        var viewModel = new SearchViewModel(backend, new LocalizationService("en"), Mock.Of<IPkgbuildService>()) { Query = "fire" };
        await viewModel.SearchCommand.Execute();
        var selected = viewModel.Results[0];
        viewModel.SelectedPackage = selected;

        await viewModel.SearchCommand.Execute();

        await Assert.That(viewModel.SelectedPackage).IsNotNull();
        await Assert.That(viewModel.SelectedPackage!.Name).IsEqualTo(selected.Name);
        await Assert.That(viewModel.SelectedDetails).IsNotNull();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        if (!condition())
        {
            throw new TimeoutException("Condition was not met within the timeout.");
        }
    }
}
