namespace eShop.BusinessRules.Client.Snapshot;

public sealed class BusinessRulesClientOptions
{
    public const string SectionName = "BusinessRules:Client";

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(60);
}
