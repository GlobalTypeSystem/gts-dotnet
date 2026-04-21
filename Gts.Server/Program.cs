using System.Text.Json;
using System.Text.Json.Serialization;
using Gts.Application;
using Gts.Store;
using Microsoft.AspNetCore.Http.Json;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(Environment.GetEnvironmentVariable("ASPNETCORE_URLS") ?? "http://127.0.0.1:8000");

builder.Services.Configure<JsonOptions>(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

var app = builder.Build();

var registry = GtsRegistry.InMemoryThreadSafe(new GtsRegistryConfig(false));
app.MapGtsApi(registry);
app.Run();
