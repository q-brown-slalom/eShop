using eShop.BusinessRules.Client.Rules.Types;
using eShop.BusinessRules.Client.Snapshot;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using ClientShortfallPolicy = eShop.BusinessRules.Client.Rules.Types.ShortfallPolicy;

namespace eShop.Ordering.API.Application.Rules;

public sealed class OrderFulfilmentRulesResolver(RulesSnapshotCache snapshotCache, TimeProvider timeProvider) : IOrderFulfilmentRulesResolver
{
    private static readonly GracePeriodRuleType GracePeriodRule = new();

    private static readonly StockShortfallPolicyRuleType ShortfallPolicyRule = new();

    public OrderFulfilmentRules Resolve()
    {
        var snapshot = snapshotCache.Current;
        var now = timeProvider.GetUtcNow();

        return new OrderFulfilmentRules(
            TimeSpan.FromMinutes(GracePeriodRule.Resolve(snapshot, now).Minutes),
            ToDomain(ShortfallPolicyRule.Resolve(snapshot, now).Policy));
    }

    // Ordering.Domain can't reference the client library, so it has its own enum with the same members (plan decision P2-D10).
    private static StockShortfallPolicy ToDomain(ClientShortfallPolicy policy) => policy switch
    {
        ClientShortfallPolicy.AllOrNothing => StockShortfallPolicy.AllOrNothing,
        ClientShortfallPolicy.PartialFulfilment => StockShortfallPolicy.PartialFulfilment,
        _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, "Unknown stock shortfall policy."),
    };
}
