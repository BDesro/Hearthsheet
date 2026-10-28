using Microsoft.AspNetCore.Components;
using Hearthsheet.Cloud;
using Hearthsheet.Core.Application;
using Hearthsheet.Web;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Logging;

// First thing: a trimmed build that can't track changes must not open a store (see App.razor).
var selfCheckPassed = ChangeTrackingSelfCheck.Passes();

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add(typeof(App), "#app", ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(App.SelfCheckPassed)] = selfCheckPassed }));
builder.RootComponents.Add<HeadOutlet>("head::after");

var log = new MemoryLogProvider(Console.WriteLine);
builder.Services.AddSingleton(log);
builder.Logging.AddProvider(log);

await builder.Build().RunAsync();
