using System.Text.Json.Serialization;

namespace eShop.ClientApp.Models.Orders;

public class OrderItem
{
    public long ProductId { get; set; }
    
    public Guid? OrderId { get; set; }

    [JsonPropertyName("unitprice")] 
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("productname")] 
    public string ProductName { get; set; }

    [JsonPropertyName("pictureurl")] 
    public string PictureUrl { get; set; }

    [JsonPropertyName("quantity")] 
    public int Quantity { get; set; }

    public decimal Discount { get; set; }

    [JsonPropertyName("isUnfulfillable")]
    public bool IsUnfulfillable { get; set; }

    public decimal Total => IsUnfulfillable ? 0 : Quantity * UnitPrice - Discount;

    public override string ToString()
    {
        return String.Format("Product Id: {0}, Quantity: {1}", ProductId, Quantity);
    }
}
