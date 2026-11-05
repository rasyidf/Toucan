using Toucan.Sample.SourceServer;

// dotnet run --project samples/Toucan.Sample.SourceServer -- --urls http://localhost:5199
// Token: TOUCAN_SERVER_TOKEN (default "reference-token"). Project id: demo.
var token = Environment.GetEnvironmentVariable("TOUCAN_SERVER_TOKEN") ?? ReferenceServer.DefaultToken;
var app = ReferenceServer.Build(args, token: token);
Console.WriteLine($"Toucan source protocol reference server. Project: demo. Token: {token}");
await app.RunAsync();
