using System.Net;
using Client.Middlewares;
using Client.Options;
using Client.Services;
using Core;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Orleans.Configuration;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<ExceptionInterceptor>();
});
builder.Services.AddGrpcReflection();

builder.Services.Configure<RouteOptions>(options => options.SetParameterPolicy<RegexInlineRouteConstraint>("regex"));

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddMemoryCache();

builder.Services
    .AddOptions<ClientOptions>()
    .Bind(builder.Configuration.GetSection(nameof(ClientOptions)))
    .ValidateDataAnnotations();

var clientOptions = builder.Configuration.GetSection(nameof(ClientOptions)).Get<ClientOptions>()!;

builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AddServerHeader = false;
    serverOptions.Listen(IPAddress.Any, 5012, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Host.UseOrleansClient(clientBuilder =>
{
    clientBuilder.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = clientOptions.ConnectionString;
    });

    clientBuilder.Configure<GatewayOptions>(options =>
    {
        options.GatewayListRefreshPeriod = TimeSpan.FromSeconds(10);
    });
    
    clientBuilder.Configure<ClusterOptions>(options =>
    {
        options.ClusterId = Constants.ClusterId;
        options.ServiceId = Constants.ClientServiceId;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapGrpcReflectionService();
}

app.MapGrpcService<SessionService>();

await app.RunAsync();