using Microsoft.AspNetCore.Components;
using Hearthsheet.Cloud;
using Hearthsheet.Core.Application;
using Hearthsheet.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;

var log = new MemoryLogProvider(Console.WriteLine);

// First thing: a trimmed build that can't track changes must not open a store (see App.razor). A throw counts as a
// failure, so the alert renders instead of "Loading…".
bool selfCheckPassed;
try
{
    selfCheckPassed = ChangeTrackingSelfCheck.Passes();
}
catch (Exception ex)
{
    log.CreateLogger("Startup").LogError(ex, "The change-tracking self-check threw");
    selfCheckPassed = false;
}

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add(typeof(App), "#app", ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(App.SelfCheckPassed)] = selfCheckPassed }));
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddSingleton(log);
builder.Logging.AddProvider(log);

await builder.Build().RunAsync();
