using System.Threading;
using eShop.BusinessRules.API.Services;

namespace eShop.BusinessRules.FunctionalTests;

/// <summary>Stands in for Ordering.API's usage endpoint; tests set the call count or the failure.</summary>
public sealed class FakeOrderingUsageClient : IOrderingUsageClient
{
    public long Calls { get; set; }

    public Exception? Failure { get; set; }

    public void Reset()
    {
        Calls = 0;
        Failure = null;
    }

    public Task<long> GetCreateOrderV1CallsAsync(int days, CancellationToken ct)
        => Failure is { } failure ? Task.FromException<long>(failure) : Task.FromResult(Calls);
}
