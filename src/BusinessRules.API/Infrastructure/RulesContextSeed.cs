namespace eShop.BusinessRules.API.Infrastructure;

public class RulesContextSeed : IDbSeeder<RulesContext>
{
    // Phase 1 ships no production rule types (D1), so there is no reference data to seed.
    public Task SeedAsync(RulesContext context) => Task.CompletedTask;
}
