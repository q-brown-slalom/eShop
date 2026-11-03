using Aspire.Hosting.Dotnet;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Foundry;
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Yarp;
using Aspire.Hosting.Yarp.Transforms;
using Microsoft.Extensions.Configuration;
using Yarp.ReverseProxy.Configuration;

namespace eShop.AppHost;

internal static class Extensions
{
    public static bool IsFoundryEnabled(IConfiguration configuration) =>
        bool.TryParse(configuration["UseFoundry"], out var useFoundry) && useFoundry;

    /// <summary>
    /// Adds a hook to set the ASPNETCORE_FORWARDEDHEADERS_ENABLED environment variable to true for all projects in the application.
    /// </summary>
    public static IDistributedApplicationBuilder AddForwardedHeaders(this IDistributedApplicationBuilder builder)
    {
        builder.Services.TryAddEventingSubscriber<AddForwardHeadersSubscriber>();
        return builder;
    }

    private class AddForwardHeadersSubscriber : IDistributedApplicationEventingSubscriber
    {
        public Task SubscribeAsync(IDistributedApplicationEventing eventing, DistributedApplicationExecutionContext executionContext, CancellationToken cancellationToken)
        {
            eventing.Subscribe<BeforeStartEvent>((@event, ct) =>
            {
                foreach (var p in @event.Model.Resources.OfType<DotnetProjectResource>())
                {
                    p.Annotations.Add(new EnvironmentCallbackAnnotation(context =>
                    {
                        context.EnvironmentVariables["ASPNETCORE_FORWARDEDHEADERS_ENABLED"] = "true";
                    }));
                }

                return Task.CompletedTask;
            });

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Configures eShop projects to use Microsoft Foundry for text embedding and chat.
    /// </summary>
    public static IDistributedApplicationBuilder AddFoundry(this IDistributedApplicationBuilder builder,
        IResourceBuilder<DotnetProjectResource> catalogApi,
        IResourceBuilder<DotnetProjectResource> webApp)
    {
        var foundry = builder.AddFoundry("foundry");
        var chat = foundry.AddDeployment("chatModel", "gpt-4.1-mini", "2025-04-14", "OpenAI");
        var textEmbedding = foundry.AddDeployment("textEmbeddingModel", "text-embedding-3-small", "1", "OpenAI");

        catalogApi.WithReference(textEmbedding).WaitFor(textEmbedding);
        webApp.WithReference(chat).WaitFor(chat);

        return builder;
    }

    /// <summary>
    /// Configures eShop projects to use Ollama for text embedding and chat.
    /// </summary>
    public static IDistributedApplicationBuilder AddOllama(this IDistributedApplicationBuilder builder,
        IResourceBuilder<DotnetProjectResource> catalogApi,
        IResourceBuilder<DotnetProjectResource> webApp)
    {
        var ollama = builder.AddOllama("ollama")
            .WithDataVolume()
            .WithGPUSupport()
            .WithOpenWebUI();
        var embeddings = ollama.AddModel("embedding", "all-minilm");
        var chat = ollama.AddModel("chat", "llama3.1");

        catalogApi.WithReference(embeddings)
            .WithEnvironment("OllamaEnabled", "true")
            .WaitFor(embeddings);
        webApp.WithReference(chat)
            .WithEnvironment("OllamaEnabled", "true")
            .WaitFor(chat);

        return builder;
    }

    /// <summary>
    /// Adds the Business Rules service (T3) and its database, and wires the service-to-service clients.
    /// Ordering.API references it but must never wait for it.
    /// </summary>
    public static IResourceBuilder<DotnetProjectResource> AddBusinessRules(this IDistributedApplicationBuilder builder,
        IResourceBuilder<PostgresServerResource> postgres,
        IResourceBuilder<RabbitMQServerResource> rabbitMq,
        IResourceBuilder<DotnetProjectResource> orderingApi,
        IResourceBuilder<DotnetProjectResource> identityApi,
        EndpointReference identityEndpoint)
    {
        var rulesDb = postgres.AddDatabase("rulesdb");
        var rulesReaderSecret = builder.AddParameter("businessrules-reader-secret", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
        var rulesApiSecret    = builder.AddParameter("businessrules-api-secret",    new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);
        var businessRulesApi = builder.AddDotnetProject("businessrules-api", "../BusinessRules.API")
            .WithReference(rabbitMq).WaitFor(rabbitMq)
            .WithReference(rulesDb).WaitFor(rulesDb)
            .WithReference(orderingApi)                     // usage guard (D3); no WaitFor
            .WithHttpHealthCheck("/health")
            .WithEnvironment("Identity__Url", identityEndpoint)
            .WithEnvironment("ServiceClient__ClientSecret", rulesApiSecret);
        orderingApi.WithReference(businessRulesApi)          // NO WaitFor: T1 must not depend on T3
            .WithEnvironment("ServiceClient__ClientSecret", rulesReaderSecret);
        identityApi.WithEnvironment("BusinessRulesApiClient", businessRulesApi.GetEndpoint("http"))
            .WithEnvironment("BusinessRulesReaderSecret", rulesReaderSecret)
            .WithEnvironment("BusinessRulesApiSecret", rulesApiSecret);

        return businessRulesApi;
    }

    public static IResourceBuilder<YarpResource> ConfigureMobileBffRoutes(this IResourceBuilder<YarpResource> builder,
        IResourceBuilder<DotnetProjectResource> catalogApi,
        IResourceBuilder<DotnetProjectResource> orderingApi,
        IResourceBuilder<DotnetProjectResource> identityApi)
    {
        return builder.WithConfiguration(yarp =>
        {
            var catalogCluster = yarp.AddCluster(catalogApi);

            yarp.AddRoute("/catalog-api/api/catalog/items", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/by", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/{id}", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/by/{name}", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/withsemanticrelevance/{text}", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/withsemanticrelevance", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["2.0"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/type/{typeId}/brand/{brandId?}", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/type/all/brand/{brandId?}", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/catalogTypes", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/catalogBrands", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            yarp.AddRoute("/catalog-api/api/catalog/items/{id}/pic", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }])
                .WithTransformPathRemovePrefix("/catalog-api");

            // Generic catalog catch-all route
            yarp.AddRoute("/api/catalog/{*any}", catalogCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }]);

            // Ordering routes (orders v2 maps the full orders API, D6)
            var orderingCluster = yarp.AddCluster(orderingApi.GetEndpoint("http"));

            yarp.AddRoute("/api/orders/{*any}", orderingCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1", "2.0"], Mode = QueryParameterMatchMode.Exact }]);

            yarp.AddRoute("/api/pricing/quote", orderingCluster)
                .WithMatchRouteQueryParameter([new() { Name = "api-version", Values = ["1.0", "1"], Mode = QueryParameterMatchMode.Exact }]);

            // No businessrules-api route: the rules snapshot is internal-only.

            // Identity routes
            yarp.AddRoute("/identity/{*any}", identityApi.GetEndpoint("http"))
                .WithTransformPathRemovePrefix("/identity");
        });
    }
}
