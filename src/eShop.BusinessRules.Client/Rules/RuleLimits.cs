namespace eShop.BusinessRules.Client.Rules;

public static class RuleLimits
{
    /// <summary>Inclusive upper bound for money values in rule parameters, keeping price arithmetic well inside decimal range.</summary>
    public const decimal MaxMoney = 1_000_000m;
}
