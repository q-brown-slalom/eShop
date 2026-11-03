using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace eShop.BusinessRules.Client.Snapshot;

public sealed class HttpRulesSnapshotSource(HttpClient httpClient) : IRulesSnapshotSource
{
    private const string SnapshotPath = "api/rules/snapshot?api-version=1.0";

    public async Task<SnapshotFetchResult> FetchAsync(long? knownVersion, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SnapshotPath);
        if (knownVersion is { } version)
        {
            request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue($"\"{version.ToString(CultureInfo.InvariantCulture)}\""));
        }

        using var response = await httpClient.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new SnapshotFetchResult(true, null);
        }

        response.EnsureSuccessStatusCode();

        var snapshot = await response.Content.ReadFromJsonAsync<RulesSnapshot>(RulesSnapshotJson.Options, ct)
            ?? throw new InvalidOperationException("The rules snapshot response body was empty.");

        return new SnapshotFetchResult(false, snapshot);
    }
}
