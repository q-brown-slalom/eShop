using eShop.ClientApp.Services.RequestProvider;

namespace ClientApp.UnitTests.Mocks;

public class MockRequestProvider : IRequestProvider
{
    public string? LastUri { get; private set; }

    public object? LastData { get; private set; }

    public string? LastHeader { get; private set; }

    public object? Response { get; set; }

    public Exception? PostException { get; set; }

    public Task<TResult> GetAsync<TResult>(string uri, string token = "")
    {
        LastUri = uri;
        return Task.FromResult(Response is TResult result ? result : default!);
    }

    public Task<TResponse> PostAsync<TRequest, TResponse>(string uri, TRequest data, string token = "", string header = "")
    {
        Record(uri, data, header);
        return Task.FromResult(Response is TResponse response ? response : default!);
    }

    public Task<bool> PostAsync<TRequest>(string uri, TRequest data, string token = "", string header = "")
    {
        Record(uri, data, header);
        return Task.FromResult(true);
    }

    public Task<TResult> PostAsync<TResult>(string uri, string data, string clientId, string clientSecret) =>
        throw new NotSupportedException();

    public Task<TResult> PutAsync<TResult>(string uri, TResult data, string token = "", string header = "")
    {
        Record(uri, data, header);
        return Task.FromResult(data);
    }

    public Task DeleteAsync(string uri, string token = "") => throw new NotSupportedException();

    private void Record(string uri, object? data, string header)
    {
        LastUri = uri;
        LastData = data;
        LastHeader = header;

        if (PostException is not null)
        {
            throw PostException;
        }
    }
}
