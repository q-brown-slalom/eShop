namespace eShop.BusinessRules.API.Services;

/// <summary>Reads API usage counts from Ordering.API (plan decision D3).</summary>
public interface IOrderingUsageClient
{
    /// <summary>Returns how many times the v1 create-order endpoint was called over the last <paramref name="days"/> UTC days.</summary>
    /// <exception cref="HttpRequestException">Ordering.API could not be reached or refused the request.</exception>
    Task<long> GetCreateOrderV1CallsAsync(int days, CancellationToken ct);
}
