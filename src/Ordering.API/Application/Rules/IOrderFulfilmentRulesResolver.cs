namespace eShop.Ordering.API.Application.Rules;

public interface IOrderFulfilmentRulesResolver
{
    /// <summary>Resolves the grace period and stock shortfall policy from the cached rules snapshot. Never calls BusinessRules.API.</summary>
    OrderFulfilmentRules Resolve();
}
