namespace eShop.BusinessRules.Client.Rules.Types;

public sealed class ShippingFeeRuleType : RuleType<ShippingFeeParameters>
{
    public const string TypeId = "SHP-001";

    public const decimal MaxFee = 100m;

    public override string Id => TypeId;

    public override string Name => "Shipping fee";

    public override bool AffectsPrice => true;

    public override RuleTargeting Targeting => RuleTargeting.Global;

    public override ShippingFeeParameters DefaultParameters { get; } = new(0m, null);

    public override string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "fee": { "type": "number", "minimum": 0, "maximum": 100, "multipleOf": 0.01 },
            "freeShippingThreshold": { "type": ["number", "null"], "minimum": 0, "multipleOf": 0.01 }
          },
          "additionalProperties": false
        }
        """;

    protected override IEnumerable<string> ValidateParameters(ShippingFeeParameters parameters)
    {
        if (parameters.Fee is < 0m or > MaxFee)
        {
            yield return $"'fee' must be between 0 and {MaxFee}.";
        }

        if (!HasAtMostTwoDecimals(parameters.Fee))
        {
            yield return "'fee' must have at most 2 decimal places.";
        }

        if (parameters.FreeShippingThreshold is { } threshold)
        {
            if (threshold < 0m)
            {
                yield return "'freeShippingThreshold' must be 0 or more.";
            }

            if (!HasAtMostTwoDecimals(threshold))
            {
                yield return "'freeShippingThreshold' must have at most 2 decimal places.";
            }
        }
    }
}

public sealed record ShippingFeeParameters(decimal Fee, decimal? FreeShippingThreshold);
