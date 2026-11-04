namespace eShop.BusinessRules.Client.Rules.Types;

public sealed class PriceAdjustmentRuleType : RuleType<PriceAdjustmentParameters>
{
    public const string TypeId = "PRC-001";

    public const int MaxEntries = 500;

    public const decimal MaxPercentOff = 90m;

    public override string Id => TypeId;

    public override string Name => "Price adjustment";

    public override bool AffectsPrice => true;

    public override RuleTargeting Targeting => RuleTargeting.Targeted;

    public override PriceAdjustmentParameters DefaultParameters { get; } = new([]);

    public override string ParameterSchemaJson => """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "properties": {
            "entries": {
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
                  "mode": { "enum": ["PercentOff", "FixedPrice"] },
                  "value": { "type": "number", "multipleOf": 0.01 }
                },
                "required": ["target", "mode", "value"],
                "additionalProperties": false
              }
            }
          },
          "required": ["entries"],
          "additionalProperties": false
        }
        """;

    protected override IEnumerable<string> ValidateParameters(PriceAdjustmentParameters parameters)
    {
        if (parameters.Entries is null)
        {
            yield return "entries: required.";
            yield break;
        }

        if (parameters.Entries.Count > MaxEntries)
        {
            yield return $"entries: at most {MaxEntries} entries are allowed.";
        }

        var firstIndexByTarget = new Dictionary<(RuleTargetKind, int?), int>();
        for (var i = 0; i < parameters.Entries.Count; i++)
        {
            var entry = parameters.Entries[i];
            var path = $"entries[{i}]";
            if (entry is null)
            {
                yield return $"{path}: must be an object.";
                continue;
            }

            var targetErrors = RuleTarget.Validate(entry.Target, $"{path}.target").ToList();
            foreach (var error in targetErrors)
            {
                yield return error;
            }

            if (targetErrors.Count == 0 && !firstIndexByTarget.TryAdd((entry.Target.Kind, entry.Target.Id), i))
            {
                yield return $"{path}.target: duplicates entries[{firstIndexByTarget[(entry.Target.Kind, entry.Target.Id)]}].target.";
            }

            if (entry.Mode == PriceAdjustmentMode.PercentOff)
            {
                if (entry.Value is < 0m or > MaxPercentOff)
                {
                    yield return $"{path}.value: percent off must be between 0 and {MaxPercentOff}.";
                }
            }
            else
            {
                if (entry.Value <= 0m)
                {
                    yield return $"{path}.value: fixed price must be greater than 0.";
                }

                if (!HasAtMostTwoDecimals(entry.Value))
                {
                    yield return $"{path}.value: fixed price must have at most 2 decimal places.";
                }
            }
        }
    }
}

public enum PriceAdjustmentMode
{
    PercentOff,
    FixedPrice,
}

public sealed record PriceAdjustmentEntry(RuleTarget Target, PriceAdjustmentMode Mode, decimal Value);

public sealed record PriceAdjustmentParameters(IReadOnlyList<PriceAdjustmentEntry> Entries);
