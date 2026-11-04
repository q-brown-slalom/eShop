namespace eShop.BusinessRules.Client.Rules.Types;

public sealed class GracePeriodRuleType : RuleType<GracePeriodParameters>
{
    public const string TypeId = "ORD-001";

    public const int MinMinutes = 1;

    public const int MaxMinutes = 60;

    public override string Id => TypeId;

    public override string Name => "Grace period";

    public override bool AffectsPrice => false;

    public override RuleTargeting Targeting => RuleTargeting.Global;

    public override GracePeriodParameters DefaultParameters { get; } = new(MinMinutes);

    public override string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "minutes": { "type": "integer", "minimum": 1, "maximum": 60 }
          },
          "required": ["minutes"],
          "additionalProperties": false
        }
        """;

    protected override IEnumerable<string> ValidateParameters(GracePeriodParameters parameters)
    {
        if (parameters.Minutes is < MinMinutes or > MaxMinutes)
        {
            yield return $"'minutes' must be between {MinMinutes} and {MaxMinutes}.";
        }
    }
}

public sealed record GracePeriodParameters(int Minutes);
