namespace eShop.BusinessRules.UnitTests;

/// <summary>A price-affecting test rule type, so publishing it goes through the v1-clients publish guard.</summary>
public sealed class TestPriceRuleType : IRuleType
{
    public const string TypeId = "TST-PRC";

    public string Id => TypeId;

    public string Name => "Test price rule";

    public bool AffectsPrice => true;

    public RuleTargeting Targeting => RuleTargeting.Global;

    public JsonElement Default { get; } = JsonDocument.Parse("""{"enabled":false}""").RootElement.Clone();

    public string ParameterSchemaJson => """{"type":"object","properties":{"enabled":{"type":"boolean"}},"required":["enabled"]}""";

    public IReadOnlyList<string> Validate(JsonElement parameters)
        => parameters.ValueKind == JsonValueKind.Object
            && parameters.TryGetProperty("enabled", out var enabled)
            && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? []
            : ["'enabled' must be a boolean."];
}
