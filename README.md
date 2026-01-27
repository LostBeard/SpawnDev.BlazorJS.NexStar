# SpawnDev.BlazorJS.NexStar
Provides tools to communicate with and control Celestron NexStar telescopes using the [Web Serial API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Serial_API) in Blazor WebAssembly.
SpawnDev.BlazorJS.NexStar is built on top of [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS) which provides full Javascript interop.

[![NuGet version](https://badge.fury.io/nu/SpawnDev.BlazorJS.NexStar.svg?label=SpawnDev.BlazorJS.NexStar)](https://www.nuget.org/packages/SpawnDev.BlazorJS.NexStar)

### Web App
[Live App](https://lostbeard.github.io/SpawnDev.BlazorJS.NexStar/)

### Getting started

Example Program.cs 
```cs
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.NexStar;
using SpawnDev.BlazorJS.NexStar.App;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");
// Add BlazorJSRunAsync service
builder.Services.AddBlazorJSRuntime();
// Add NexStarService service
builder.Services.AddSingleton<NexStarService>();
// initialize BlazorJSRuntime to start app
await builder.Build().BlazorJSRunAsync();
```

