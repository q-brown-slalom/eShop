namespace eShop.BusinessRules.Client.Pricing;

public static class PriceRounding
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
