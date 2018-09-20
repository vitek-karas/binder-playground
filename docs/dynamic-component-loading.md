# Dynamic component loading

## Problem statement
.NET Core runtime has only limited support for dynamically loading assemblies which have additional dependencies or possibly collide with the app in any way. Out of the box only these scenarios really work:
* Assemblies which have no additional dependencies other than those find in the app itself (`Assembly.Load` and `Assembly.LoadFrom`)
* Assemblies which have additional dependencies in the same folder and which don't collide with anything in the app itself (`Assembly.LoadFrom`)
* Loading a single assembly in full isolation (from the app), but all its dependencies must come from the app (`Assembly.LoadFile`)

Other scenarios are technically supported by implementing a custom `AssemblyLoadContext` but doing so is complex.  
Additionally, there's no inherent synergy with the .NET Core SDK tooling. Components produced by the SDK can't be easily loaded at runtime.

## Scenarios
List of few scenarios where dynamic loading of full components is required:
* MSBuild tasks - all tasks in MSBuild are dynamically loaded. Some tasks come with additional dependencies which can collide with each other or MSBuild itself as well.
* Roslyn analyzers - similar to MSBuild tasks, the Roslyn compiler dynamically loads analyzers which are separate components with potentially conflicting dependencies.
* XUnit loading tests - the test runner acts as an app and the test is loaded dynamically. The test can have any number of dependencies. Finding and resolving those dependencies is challenging.
* ASP .NET's `dotnet watch` - ability to dynamically reload an app without restarting the process. Each version of the app is inherently in collision with any previous version. The old version should be unloaded.

In lot of these cases the component which is to be loaded dynamically has a non-trivial amount of dependencies which are unknown to the app itself. So the loading mechanism has to be able to resolve them.  
The SDK tooling uses the `.deps.json` format to describe dependencies of a component, so it would be beneficial if the dynamic load was able to use this same format for dependency resolution.

## Dynamic loading with dependencies
We propose to add a new public API which would dynamically load a component with these properties:
* Component is loaded in isolation from the app (and other components) so that potential collisions are not an issue
* Component can use `.deps.json` to describe its dependencies. This includes the ability to describe additional NuGet packages, RID-specific and/or native dependencies.
* Component can chose to rely on the app for certain dependencies by not including them in its `.deps.json`
* Optionally such component can be enabled for unloading

Public API (early thinking):
* `static Assembly Assembly.LoadFileWithDependencies` - in its core similar to `Assembly.LoadFile` but it supports resolving dependencies through `.deps.json` and such. Just like `Assembly.LoadFile` it provides isolation, but also for the dependencies.
* `static AssemblyLoadContext AssemblyLoadContext.CreateForAssemblyWithDependencies(string assemblyPath)` - "advanced" version which would return an ALC instance and not just the main assembly. Could have overloads for enabling unloadability.

## High-level description of the solution
* Implement a new `AssemblyLoadContext` which will provide the isolation boundary and act as the "root" for the component. It can be enabled for unloading.  
* The new load context is initialized by specifying the full path to the main assembly of the component to load.  
* It will look for the `.deps.json` next to that assembly to determine its dependencies. Lack of `.deps.json` will be treated in the same way it is today for executable apps - that is all the assemblies in the same folder will be used as dependencies.
* Parsing and understanding of the `.deps.json` will be performed by the same host components which do this for executable apps (so same behavior/quirks/bugs, very little code duplication).
* If the component has `.runtimeconfig.json` it will only be used to verify runtime version and provide probing paths.
* The load context will determine a list of assemblies similar to TPA and list of native search paths. These will be used to resolve any assembly binding events in the load context.
* If the load context can't resolve an assembly bind event, it will fallback to the parent load context (the app)

## Important implications and limitations
* Only framework dependent components will be supported. Self-contained components will not be supported even if there was a way to produce them.
* The host (the app) can be any configuration (framework dependent or self-contained). The notion of frameworks is completely ignored by this new functionality.
* All framework dependencies of the component must be resolvable by the app - simply put, the component must use the same frameworks as the app.
* Components can't add frameworks to the app - the app must "pre-load" all necessary frameworks.
* By default only framework types will be shared between the app and the component. The component may chose to share more by not including dependencies in the component (`CopyLocal=false`).
* Pretty much all settings in `.runtimeconfig.json` and `.runtimeconfig.dev.json` will be ignored with the exception of runtime version (probably done through TFM) and additional probing paths.