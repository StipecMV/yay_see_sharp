using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using TUnit.Core;

namespace yay_see_sharp.integration.Tests;

[Category("Integration")]
public class AurSearchIntegrationTests
{
    private static readonly TimeSpan NetworkTimeout = TimeSpan.FromSeconds(10);

    private sealed record AurSearchResponse(
        [property: JsonPropertyName("resultcount")] int ResultCount,
        [property: JsonPropertyName("results")] List<AurPackageResult> Results);

    private sealed record AurPackageResult(string Name, string Version, int NumVotes, string? Description);

    [Test]
    public async Task Aur_rpc_search_returns_packages_with_name_version_votes_and_description()
    {
        var response = await IntegrationSkip.RunOrSkipOnNetworkFailureAsync(async () =>
        {
            using var client = new HttpClient();
            var json = await client.GetStringAsync("https://aur.archlinux.org/rpc/v5/search/firefox");
            return JsonSerializer.Deserialize<AurSearchResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }, NetworkTimeout);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.Results).IsNotEmpty();

        var first = response.Results[0];
        await Assert.That(first.Name).IsNotNull();
        await Assert.That(first.Name).IsNotEmpty();
        await Assert.That(first.Version).IsNotNull();
        await Assert.That(first.Version).IsNotEmpty();
        await Assert.That(first.NumVotes).IsGreaterThanOrEqualTo(0);
        await Assert.That(first.Description).IsNotNull();
    }

    [Test]
    public async Task Aur_rpc_search_for_a_nonsense_query_returns_zero_results_without_erroring()
    {
        var response = await IntegrationSkip.RunOrSkipOnNetworkFailureAsync(async () =>
        {
            using var client = new HttpClient();
            var json = await client.GetStringAsync("https://aur.archlinux.org/rpc/v5/search/zzz-definitely-not-a-real-package-zzz-123456");
            return JsonSerializer.Deserialize<AurSearchResponse>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });
        }, NetworkTimeout);

        await Assert.That(response).IsNotNull();
        await Assert.That(response!.ResultCount).IsEqualTo(0);
    }
}
