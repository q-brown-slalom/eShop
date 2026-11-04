namespace eShop.BusinessRules.Client.Rules.Types;

public sealed class PromotionRuleType : RuleType<PromotionParameters>
{
    public const string TypeId = "PRM-001";

    public const int MaxPromotions = 500;

    public const int MaxLabelLength = 60;

    public override string Id => TypeId;

    public override string Name => "Promotion";

    public override bool AffectsPrice => true;

    public override RuleTargeting Targeting => RuleTargeting.Targeted;

    public override PromotionParameters DefaultParameters { get; } = new([], []);

    public override string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "linePromotions": {
              "type": "array",
              "maxItems": 500,
              "items": {
                "type": "object",
                "properties": {
                  "target": {
                    "type": "object",
                    "properties": {
                      "kind": { "enum": ["All", "Brand", "Type", "Item"] },
                      "id": { "type": ["integer", "null"], "minimum": 1 }
                    },
                    "required": ["kind"],
                    "additionalProperties": false
                  },
                  "kind": { "enum": ["PercentOff", "AmountOff"] },
                  "value": { "type": "number", "exclusiveMinimum": 0, "maximum": 1000000 },
                  "minUnits": { "type": ["integer", "null"], "minimum": 1 },
                  "label": { "type": "string", "minLength": 1, "maxLength": 60, "pattern": "^[^<>]*$" }
                },
                "required": ["target", "kind", "value", "label"],
                "additionalProperties": false
              }
            },
            "orderPromotions": {
              "type": "array",
              "maxItems": 500,
              "items": {
                "type": "object",
                "properties": {
                  "kind": { "enum": ["PercentOff", "AmountOff"] },
                  "value": { "type": "number", "exclusiveMinimum": 0, "maximum": 1000000 },
                  "minSubtotal": { "type": "number", "minimum": 0, "maximum": 1000000, "multipleOf": 0.01 },
                  "label": { "type": "string", "minLength": 1, "maxLength": 60, "pattern": "^[^<>]*$" }
                },
                "required": ["kind", "value", "minSubtotal", "label"],
                "additionalProperties": false
              }
            }
          },
          "required": ["linePromotions", "orderPromotions"],
          "additionalProperties": false
        }
        """;

    protected override IEnumerable<string> ValidateParameters(PromotionParameters parameters)
    {
        foreach (var error in ValidateLinePromotions(parameters.LinePromotions))
        {
            yield return error;
        }

        foreach (var error in ValidateOrderPromotions(parameters.OrderPromotions))
        {
            yield return error;
        }
    }

    private static IEnumerable<string> ValidateLinePromotions(IReadOnlyList<LinePromotion> promotions)
    {
        if (promotions is null)
        {
            yield return "linePromotions: required.";
            yield break;
        }

        if (promotions.Count > MaxPromotions)
        {
            yield return $"linePromotions: at most {MaxPromotions} promotions are allowed.";
        }

        for (var i = 0; i < promotions.Count; i++)
        {
            var promotion = promotions[i];
            var path = $"linePromotions[{i}]";
            if (promotion is null)
            {
                yield return $"{path}: must be an object.";
                continue;
            }

            foreach (var error in RuleTarget.Validate(promotion.Target, $"{path}.target")
                .Concat(ValidateDiscount(promotion.Kind, promotion.Value, path))
                .Concat(ValidateLabel(promotion.Label, path)))
            {
                yield return error;
            }

            if (promotion.MinUnits is < 1)
            {
                yield return $"{path}.minUnits: must be 1 or more.";
            }
        }
    }

    private static IEnumerable<string> ValidateOrderPromotions(IReadOnlyList<OrderPromotion> promotions)
    {
        if (promotions is null)
        {
            yield return "orderPromotions: required.";
            yield break;
        }

        if (promotions.Count > MaxPromotions)
        {
            yield return $"orderPromotions: at most {MaxPromotions} promotions are allowed.";
        }

        for (var i = 0; i < promotions.Count; i++)
        {
            var promotion = promotions[i];
            var path = $"orderPromotions[{i}]";
            if (promotion is null)
            {
                yield return $"{path}: must be an object.";
                continue;
            }

            foreach (var error in ValidateDiscount(promotion.Kind, promotion.Value, path)
                .Concat(ValidateLabel(promotion.Label, path)))
            {
                yield return error;
            }

            if (promotion.MinSubtotal is < 0m or > RuleLimits.MaxMoney)
            {
                yield return $"{path}.minSubtotal: must be between 0 and {RuleLimits.MaxMoney:0}.";
            }

            if (!HasAtMostTwoDecimals(promotion.MinSubtotal))
            {
                yield return $"{path}.minSubtotal: must have at most 2 decimal places.";
            }
        }
    }

    private static IEnumerable<string> ValidateDiscount(DiscountKind kind, decimal value, string path)
    {
        if (kind == DiscountKind.PercentOff)
        {
            if (value is <= 0m or > 100m)
            {
                yield return $"{path}.value: percent off must be greater than 0 and at most 100.";
            }
        }
        else
        {
            if (value is <= 0m or > RuleLimits.MaxMoney)
            {
                yield return $"{path}.value: amount off must be between 0.01 and {RuleLimits.MaxMoney:0}.";
            }

            if (!HasAtMostTwoDecimals(value))
            {
                yield return $"{path}.value: amount off must have at most 2 decimal places.";
            }
        }
    }

    private static IEnumerable<string> ValidateLabel(string? label, string path)
    {
        // Raw length: the calculator stores the label unmodified in a varchar(MaxLabelLength) column.
        if (label is null || label.Length > MaxLabelLength || string.IsNullOrWhiteSpace(label))
        {
            yield return $"{path}.label: must be 1 to {MaxLabelLength} characters.";
        }

        if (label is not null && label.AsSpan().ContainsAny('<', '>'))
        {
            yield return $"{path}.label: must not contain '<' or '>'.";
        }
    }
}

public enum DiscountKind
{
    PercentOff,
    AmountOff,
}

public sealed record LinePromotion(RuleTarget Target, DiscountKind Kind, decimal Value, int? MinUnits, string Label);

public sealed record OrderPromotion(DiscountKind Kind, decimal Value, decimal MinSubtotal, string Label);

public sealed record PromotionParameters(IReadOnlyList<LinePromotion> LinePromotions, IReadOnlyList<OrderPromotion> OrderPromotions);
