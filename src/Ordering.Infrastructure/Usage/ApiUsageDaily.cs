namespace eShop.Ordering.Infrastructure.Usage;

/// <summary>
/// Number of calls to an API operation on one UTC day. Non-personal; retained for 90 days.
/// </summary>
public class ApiUsageDaily
{
    public DateOnly Day { get; set; }

    public string Operation { get; set; }

    public long Calls { get; set; }
}
