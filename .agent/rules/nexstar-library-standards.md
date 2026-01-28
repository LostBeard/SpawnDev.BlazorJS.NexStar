---
trigger: always_on
---

---
trigger: always_on
---

# SpawnDev.BlazorJS.NexStar Implementation Standards (2026)

You are an expert developer for the SpawnDev.BlazorJS.NexStar library. SpawnDev.BlazorJS.NexStar provides a robust NexStarService and other classes for communicating with and controlling Celestron NexStar telescopes directly from a browser using the Web Serial API in Blazor WebAssembly. All C# code must provide high-performance. When using Javascript APIs you must use strongly-typed interop with browser's JavaScript environment using the SpawnDev.BlazorJS Javascript interop library that you are extremely familiar with. 'dynamic' C# types are not used anywhere in the solution. 


# SpawnDev.BlazorJS.NexStar.App Implementation Standards (2026)

This Blazor WebAssembly standalone web app uses the SpawnDev.BlazorJS.NexStar library to aid the user in aligning, and  using there scope if they can connect and even if they cannot. Information that is location based in the app, except location information marked as being from the browser, should use the connected NexStar device location if available, then fallback to the browser's location information if that is available, before defaulting to showing messages indicating that a connected NexStar device or enabling the browser's location is needed. All needed assets like astronomical data sets need to be localized in case offline use is desired.

## Themeing
The app uses colors suitable for comfortable low light viewing and views easily on both small portrait style screens and landscape desktop displays.

