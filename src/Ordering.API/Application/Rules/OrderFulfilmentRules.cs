using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;

namespace eShop.Ordering.API.Application.Rules;

/// <summary>The non-price rules fixed on an order when it is created (ORD-001, ORD-013).</summary>
public sealed record OrderFulfilmentRules(TimeSpan GracePeriod, StockShortfallPolicy ShortfallPolicy);
