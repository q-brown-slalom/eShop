using eShop.ClientApp.Models.Pricing;

namespace eShop.ClientApp.Models.Orders;

public sealed record CreateOrderV2Request(
    string UserId,
    string UserName,
    string City,
    string Street,
    string State,
    string Country,
    string ZipCode,
    string CardNumber,
    string CardHolderName,
    DateTime CardExpiration,
    string CardSecurityNumber,
    int CardTypeId,
    string Buyer,
    IReadOnlyList<QuoteLine> Items,
    decimal ExpectedTotal);
