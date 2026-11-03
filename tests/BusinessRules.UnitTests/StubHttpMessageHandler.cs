namespace eShop.BusinessRules.UnitTests;

internal sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public List<string?> RequestBodies { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
        return respond(request);
    }
}
