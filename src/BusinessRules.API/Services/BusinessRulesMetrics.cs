using System.Diagnostics.Metrics;

namespace eShop.BusinessRules.API.Services;

public sealed class BusinessRulesMetrics
{
    public const string MeterName = "eShop.BusinessRules";

    private readonly Counter<long> _publishes;

    public BusinessRulesMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _publishes = meter.CreateCounter<long>(
            "rules.publish.count",
            unit: "{publish}",
            description: "Number of rule versions published into a new rules snapshot.");
    }

    public void RulePublished() => _publishes.Add(1);
}
