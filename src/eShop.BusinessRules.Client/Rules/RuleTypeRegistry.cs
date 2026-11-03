using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace eShop.BusinessRules.Client.Rules;

public sealed class RuleTypeRegistry
{
    private readonly Dictionary<string, IRuleType> _types;

    public RuleTypeRegistry(IEnumerable<IRuleType> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        _types = new Dictionary<string, IRuleType>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            if (!_types.TryAdd(type.Id, type))
            {
                throw new ArgumentException($"Rule type '{type.Id}' is registered more than once.", nameof(types));
            }
        }
    }

    public IReadOnlyCollection<IRuleType> All => _types.Values;

    public IRuleType? Find(string id) => _types.GetValueOrDefault(id);
}

public static class RuleTypeServiceCollectionExtensions
{
    public static IServiceCollection AddRuleType<T>(this IServiceCollection services) where T : class, IRuleType
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuleType, T>());
        services.TryAddSingleton<RuleTypeRegistry>();
        return services;
    }
}
