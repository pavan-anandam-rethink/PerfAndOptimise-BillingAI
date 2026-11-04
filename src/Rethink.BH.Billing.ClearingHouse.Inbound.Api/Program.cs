using Rethink.BH.Billing.ClearingHouse.Inbound.Api.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddInboundHost(args);

var app = builder.Build();
app.UseInboundPipeline();
await app.RunAsync().ConfigureAwait(false);

/// <summary>Entry point; public so integration tests can host the API with <c>WebApplicationFactory</c>.</summary>
public partial class Program
{
    /// <summary>Not constructed; exists for <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
    protected Program()
    {
    }
}
