using TUnit.Core;
using yay_see_sharp.domain.Models;
using yay_see_sharp.infrastructure.Http;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.integration.Tests;

[Category("Integration")]
public class PkgbuildFetchIntegrationTests
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(10);

    [Test]
    public async Task Fetching_the_pkgbuild_for_yay_returns_text_starting_with_pkgname()
    {
        var viewModel = new PkgbuildViewModel("yay", PackageSource.Aur, new PkgbuildService(), new LocalizationService("en"));

        await IntegrationSkip.RunOrSkipOnNetworkFailureAsync(async () =>
        {
            await viewModel.LoadAsync();
            return true;
        }, NetworkTimeout);

        if (viewModel.ErrorMessage is not null)
        {
            throw new TUnit.Core.Exceptions.SkipTestException($"AUR fetch failed: {viewModel.ErrorMessage}");
        }

        await Assert.That(viewModel.Content).IsNotNull();

        // Real PKGBUILDs conventionally lead with a "# Maintainer:" comment before pkgname=,
        // so look at the first non-comment, non-blank line rather than the literal file start.
        var firstMeaningfulLine = viewModel.Content!
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'));
        await Assert.That(firstMeaningfulLine).IsNotNull();
        await Assert.That(firstMeaningfulLine!.StartsWith("pkgname=", StringComparison.Ordinal)).IsTrue();
        await Assert.That(viewModel.IsLoading).IsFalse();
    }

    [Test]
    public async Task Fetching_the_pkgbuild_for_a_nonexistent_package_surfaces_an_error_instead_of_throwing()
    {
        var viewModel = new PkgbuildViewModel(
            "zzz-definitely-not-a-real-package-zzz-123456",
            PackageSource.Aur,
            new PkgbuildService(),
            new LocalizationService("en"));

        await IntegrationSkip.RunOrSkipOnNetworkFailureAsync(async () =>
        {
            await viewModel.LoadAsync();
            return true;
        }, NetworkTimeout);

        await Assert.That(viewModel.Content).IsNull();
        await Assert.That(viewModel.ErrorMessage).IsNotNull();
    }
}
