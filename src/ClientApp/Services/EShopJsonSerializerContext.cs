using System.Text.Json.Serialization;
using eShop.ClientApp.Models.Catalog;
using eShop.ClientApp.Models.Orders;
using eShop.ClientApp.Models.Pricing;
using eShop.ClientApp.Models.Token;

namespace eShop.ClientApp.Services;

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(CancelOrderCommand))]
[JsonSerializable(typeof(CatalogBrand))]
[JsonSerializable(typeof(CatalogItem))]
[JsonSerializable(typeof(CatalogRoot))]
[JsonSerializable(typeof(CatalogType))]
[JsonSerializable(typeof(CreateOrderV2Request))]
[JsonSerializable(typeof(Models.Orders.Order))]
[JsonSerializable(typeof(PriceQuote))]
[JsonSerializable(typeof(PriceQuoteRequest))]
[JsonSerializable(typeof(PricesChangedProblem))]
[JsonSerializable(typeof(Models.Location.Location))]
[JsonSerializable(typeof(UserToken))]
internal partial class EShopJsonSerializerContext : JsonSerializerContext
{
}
