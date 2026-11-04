using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using eShop.BusinessRules.Client.Snapshot;

namespace eShop.BusinessRules.Client.Rules;

/// <summary>
/// Base for rule types whose parameters bind to a <typeparamref name="TParameters"/> record.
/// Parameter JSON is camelCase, enums are strings only, and unknown members are rejected (plan decision P2-D1).
/// </summary>
public abstract class RuleType<TParameters> : IRuleType where TParameters : class
{
    protected static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    public abstract string Id { get; }

    public abstract string Name { get; }

    public abstract bool AffectsPrice { get; }

    public abstract RuleTargeting Targeting { get; }

    public abstract string ParameterSchemaJson { get; }

    public abstract TParameters DefaultParameters { get; }

    public JsonElement Default => JsonSerializer.SerializeToElement(DefaultParameters, JsonOptions);

    public IReadOnlyList<string> Validate(JsonElement parameters)
    {
        if (!TryDeserialize(parameters, out var parsed, out var error))
        {
            return [error];
        }

        return [.. ValidateParameters(parsed)];
    }

    /// <summary>True only when <paramref name="parameters"/> deserializes and passes validation.</summary>
    public bool TryParse(JsonElement parameters, [NotNullWhen(true)] out TParameters? value)
    {
        if (TryDeserialize(parameters, out var parsed, out _) && !ValidateParameters(parsed).Any())
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Returns the effective published parameters, or <see cref="DefaultParameters"/> when none is effective or the
    /// published ones don't parse or validate for this consumer. Never throws.
    /// </summary>
    public TParameters Resolve(RulesSnapshot snapshot, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var effective = snapshot.FindEffective(Id, RuleScopes.Default, now);
        return effective is not null && TryParse(effective.Parameters, out var value) ? value : DefaultParameters;
    }

    protected abstract IEnumerable<string> ValidateParameters(TParameters parameters);

    protected static bool HasAtMostTwoDecimals(decimal value) => decimal.Round(value, 2) == value;

    private bool TryDeserialize(JsonElement parameters, [NotNullWhen(true)] out TParameters? parsed, [NotNullWhen(false)] out string? error)
    {
        if (parameters.ValueKind == JsonValueKind.Undefined)
        {
            parsed = null;
            error = $"Parameters don't match the {Id} schema: a JSON object is required.";
            return false;
        }

        try
        {
            parsed = parameters.Deserialize<TParameters>(JsonOptions);
            error = parsed is null ? $"Parameters don't match the {Id} schema: a JSON object is required." : null;
            return parsed is not null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            parsed = null;
            error = $"Parameters don't match the {Id} schema: {ex.Message}";
            return false;
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
