using System.Text.Json.Serialization;
using eShop.BusinessRules.Client.Pricing;

namespace eShop.BusinessRules.Client.Rules;

/// <summary>What a targeted rule applies to. The numeric order is the specificity order: item beats type beats brand beats all.</summary>
public enum RuleTargetKind
{
    All,
    Brand,
    Type,
    Item,
}

/// <summary>A catalog target: <c>{"kind":"Type","id":1}</c>. <see cref="RuleTargetKind.All"/> has no id; the others require a positive one.</summary>
public sealed record RuleTarget(RuleTargetKind Kind, int? Id)
{
    [JsonIgnore]
    public int Specificity => (int)Kind;

    public bool Matches(CatalogPrice product)
    {
        ArgumentNullException.ThrowIfNull(product);

        return Kind switch
        {
            RuleTargetKind.All => true,
            RuleTargetKind.Brand => product.CatalogBrandId == Id,
            RuleTargetKind.Type => product.CatalogTypeId == Id,
            RuleTargetKind.Item => product.ProductId == Id,
            _ => false,
        };
    }

    internal static IEnumerable<string> Validate(RuleTarget? target, string path)
    {
        if (target is null)
        {
            yield return $"{path}: required.";
        }
        else if (target.Kind == RuleTargetKind.All)
        {
            if (target.Id is not null)
            {
                yield return $"{path}.id: must be omitted when kind is All.";
            }
        }
        else if (target.Id is not > 0)
        {
            yield return $"{path}.id: must be a positive id when kind is {target.Kind}.";
        }
    }
}
