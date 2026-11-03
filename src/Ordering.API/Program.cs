var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddApplicationServices();
builder.Services.AddProblemDetails();

var withApiVersioning = builder.Services.AddApiVersioning(options =>
{
    // Include "api-supported-versions" and "api-deprecated-versions" headers in all responses
    options.ReportApiVersions = true;
});

builder.AddDefaultOpenApi(withApiVersioning);

var app = builder.Build();

app.MapDefaultEndpoints();

var orders = app.NewVersionedApi("Orders");

orders.MapOrdersApiV1()
      .RequireAuthorization();

var usage = app.NewVersionedApi("Usage");

usage.MapUsageApiV1()
     .RequireAuthorization(OrderingPolicies.UsageReader);

var pricing = app.NewVersionedApi("Pricing");

pricing.MapPricingApiV1()
       .AllowAnonymous();

app.UseDefaultOpenApi();
app.Run();
