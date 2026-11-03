namespace eShop.BusinessRules.UnitTests;

public sealed class TestRuleType : IRuleType
{
    public const string TypeId = "TST-001";

    public string Id => TypeId;

    public string Name => "Test rule";

    public bool AffectsPrice => false;

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
