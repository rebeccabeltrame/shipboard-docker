var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres");
var shipboardDb = postgres.AddDatabase("shipboard");

var api = builder.AddDotnetProject("api", "../src/ShipBoard.Api/ShipBoard.Api.csproj")
    .WithReference(shipboardDb)
    .WaitFor(shipboardDb)
    .WithHttpHealthCheck("/health");

var web = builder.AddJavaScriptApp("web", "../web", "start")
    .WithReference(api)
    .WaitFor(api)
    .WithHttpEndpoint(env: "PORT");

// Not "$(PORT)": args are not shell-expanded, so ng would get a literal string (NaN).
web.WithArgs(ctx =>
{
    ctx.Args.Add("--");
    ctx.Args.Add("--host");
    ctx.Args.Add("0.0.0.0");
    ctx.Args.Add("--port");
    ctx.Args.Add(web.GetEndpoint("http").Property(EndpointProperty.TargetPort));
});

builder.Build().Run();
