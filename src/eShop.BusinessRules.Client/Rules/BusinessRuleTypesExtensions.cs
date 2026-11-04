using eShop.BusinessRules.Client.Rules.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace eShop.BusinessRules.Client.Rules;

public static class BusinessRuleTypesExtensions
{
    /// <summary>
    /// Registers every production rule type with the <see cref="RuleTypeRegistry"/> and as an injectable singleton.
    /// Safe to call more than once.
    /// </summary>
    public static IServiceCollection AddBusinessRuleTypes(this IServiceCollection services)
    {
        services.AddRuleType<PriceAdjustmentRuleType>();
        services.AddRuleType<PromotionRuleType>();
        services.AddRuleType<GracePeriodRuleType>();
        services.AddRuleType<StockShortfallPolicyRuleType>();
        services.AddRuleType<ShippingFeeRuleType>();

        services.TryAddSingleton<PriceAdjustmentRuleType>();
        services.TryAddSingleton<PromotionRuleType>();
        services.TryAddSingleton<GracePeriodRuleType>();
        services.TryAddSingleton<StockShortfallPolicyRuleType>();
        services.TryAddSingleton<ShippingFeeRuleType>();

        return services;
    }
}
