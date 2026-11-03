using System.Text.Json;

namespace eShop.BusinessRules.Client.Snapshot;

/// <summary>Wire format for <see cref="RulesSnapshot"/>, shared by the BusinessRules.API server and its consumers.</summary>
public static class RulesSnapshotJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
