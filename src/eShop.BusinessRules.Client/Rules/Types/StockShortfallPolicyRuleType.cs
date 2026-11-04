namespace eShop.BusinessRules.Client.Rules.Types;

public sealed class StockShortfallPolicyRuleType : RuleType<StockShortfallPolicyParameters>
{
    public const string TypeId = "ORD-013";

    public override string Id => TypeId;

    public override string Name => "Stock shortfall policy";

    public override bool AffectsPrice => false;

    public override RuleTargeting Targeting => RuleTargeting.Global;

    public override StockShortfallPolicyParameters DefaultParameters { get; } = new(ShortfallPolicy.AllOrNothing);

    public override string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "policy": { "type": "string", "enum": ["AllOrNothing", "PartialFulfilment"] }
          },
          "additionalProperties": false
        }
        """;

    protected override IEnumerable<string> ValidateParameters(StockShortfallPolicyParameters parameters)
    {
        if (!Enum.IsDefined(parameters.Policy))
        {
            yield return "'policy' must be AllOrNothing or PartialFulfilment.";
        }
    }
}

public enum ShortfallPolicy
{
    AllOrNothing,
    PartialFulfilment,
}

public sealed record StockShortfallPolicyParameters(ShortfallPolicy Policy);
