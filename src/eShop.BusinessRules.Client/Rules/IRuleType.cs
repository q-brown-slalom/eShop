using System.Text.Json;

namespace eShop.BusinessRules.Client.Rules;

/// <summary>
/// A rule type is code: it defines the parameter shape, validation, and built-in default.
/// Rule values (versions) are data published in a <see cref="Snapshot.RulesSnapshot"/>.
/// </summary>
public interface IRuleType
{
    string Id { get; }

    string Name { get; }

    bool AffectsPrice { get; }

    RuleTargeting Targeting { get; }

    /// <summary>The built-in parameters used when no version is effective.</summary>
    JsonElement Default { get; }

    string ParameterSchemaJson { get; }

    /// <summary>Returns validation errors for <paramref name="parameters"/>; empty when valid.</summary>
    IReadOnlyList<string> Validate(JsonElement parameters);
}
