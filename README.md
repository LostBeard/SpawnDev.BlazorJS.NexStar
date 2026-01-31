# SpawnDev.BlazorJS.NexStar

**SpawnDev.BlazorJS.NexStar** provides a robust `NexStarService` for communicating with and controlling Celestron NexStar telescopes directly from a browser using the [Web Serial API](https://developer.mozilla.org/en-US/docs/Web/API/Web_Serial_API) in Blazor WebAssembly.

Built on top of [SpawnDev.BlazorJS](https://github.com/LostBeard/SpawnDev.BlazorJS), this library enables full hardware access without requiring a backend server.

[![NuGet version](https://badge.fury.io/nu/SpawnDev.BlazorJS.NexStar.svg?label=SpawnDev.BlazorJS.NexStar)](https://www.nuget.org/packages/SpawnDev.BlazorJS.NexStar)

---

## 🔭 Live Demo App
**[Launch NexStar Control](https://lostbeard.github.io/SpawnDev.BlazorJS.NexStar/)**

The repository includes `SpawnDev.BlazorJS.NexStar.App`, a full-featured Progressive Web App (PWA) demonstrating the library's capabilities.

### App Features:
- **Telescope Control**: 
  - Complete directional slewing with variable rates.
  - GoTo coordinates (RA/Dec).
  - Tracking mode selection (EQ North, EQ South, Alt-Az, Off).
- **Object Browser**: 
  - Catalog of 110 Messier objects and bright alignment stars.
  - "Quick Access" panel showing objects currently visible from your location.
  - Filter by constellation, magnitude, and type.
- **Alignment Helper**: 
  - Real-time suggestions for best alignment stars based on time and location.
  - Visual tracking of alignment status.
- **Location Services**: 
  - Sync telescope time and location with browser data.
- **Fallback USB Support**: 
  - For Android devices, uses Web USB API to connect via Prolific PL2303 driver.
- **Dark Mode**: 
  - Optimized dark red/black UI for viewing in low light environments.

---

## 💻 Library Features (`SpawnDev.BlazorJS.NexStar`)

### Core Capabilities
- **Web Serial Connectivity**: Direct serial port selection and connection via browser.
- **Command Protocol**: Implementation of the Celestron NexStar communication protocol.
- **Position Tracking**: 
  - Real-time RA/Dec and Az/Alt monitoring.
  - Automatic polling and state management.
- **Astronomy Math**: 
  - Built-in utilities for coordinate conversion (Equatorial <-> Horizontal).
  - LST (Local Sidereal Time) calculation.
  - Visibility calculations based on observer location.
- **Catalogs**: 
  - Integrated database of alignment stars and Messier objects.

### Installation

```bash
dotnet add package SpawnDev.BlazorJS.NexStar
```

### Usage Example (Program.cs)

```cs
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SpawnDev.BlazorJS;
using SpawnDev.BlazorJS.NexStar;
using SpawnDev.BlazorJS.NexStar.App;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// 1. Add BlazorJSRuntime (Required for Web Serial)
builder.Services.AddBlazorJSRuntime();

// 2. Add NexStarService
builder.Services.AddSingleton<NexStarService>();

// 3. Initialize and Run
await builder.Build().BlazorJSRunAsync();
```

### Injecting the Service

```razor
@inject NexStarService NexStar

<button @onclick="Connect">Connect Telescope</button>

@code {
    private async Task Connect()
    {
        if (await NexStar.SelectPortAsync())
        {
            
        }
    }
}
```

### Requirements
- A browser with **Web Serial API** support (Chrome, Edge, Opera).
- A browser with **Web USB API** support (Chrome on Android).
- A Celestron NexStar telescope (or compatible mount).
- A valid customized USB-Serial cable or connection.

### Tested Platforms
- **Windows (Chrome)**: Verified working with Web Serial API.
- **Android (Chrome)**: Verified working with Web USB API (using minimal PL2303 driver).
- **Web Serial**: Generic Support
- **Web USB**: Generic Prolific PL2303 Support


