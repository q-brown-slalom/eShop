using System.Globalization;
using System.Net.Http.Json;

namespace eShop.BusinessRules.API.Services;

/// <summary>Calls Ordering.API's <c>GET api/usage/create-order-v1</c>; the api-version and service token are added by the client's handlers.</summary>
public sealed class OrderingUsageClient(HttpClient httpClient) : IOrderingUsageClient
{
    public async Task<long> GetCreateOrderV1CallsAsync(int days, CancellationToken ct)
    {
        var usage = await httpClient.GetFromJsonAsync<ApiUsageResponse>(
            $"api/usage/create-order-v1?days={days.ToString(CultureInfo.InvariantCulture)}", ct)
            ?? throw new InvalidOperationException("The Ordering usage response body was empty.");

        return usage.Calls;
    }

    private sealed record ApiUsageResponse(string Operation, int Days, long Calls);
}
