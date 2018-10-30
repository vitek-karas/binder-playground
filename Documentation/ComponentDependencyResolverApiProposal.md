This is a proposal to add new public API which would expose functionality to help with resolution of managed and unmanaged dependencies of components.

### Proposed Surface Area
``` C#
namespace System.Runtime.Loader
{
    public sealed class ComponentDependencyResolver
    {
        public ComponentDependencyResolver(string componentAssemblyPath);

        public string ResolveAssembly(AssemblyName assemblyName);
        public string ResolveUnmanagedDll(string unmanagedDllName);
    }
}
```

### Functionality
Given the path to a component assembly (the main `.dll` of a given component, for example the build result of a class library project), the constructor creates a resolver object which can resolve managed and unmanaged dependencies of the component. The constructor would look for the `.deps.json` file next to the main assembly and use it to compute the set of dependencies.

The `ResolveAssembly` and `ResolveUnmanagedDll` methods are then used to resolve references to managed and unmanaged dependencies. These methods take the name of the dependency and return either null if such dependency can't be resolved by the component, or a full path to the file (managed assembly or unmanaged library).

The constructor is expected to catch most error cases and report them as exceptions. The `Resolve` methods should in general not throw and instead return null if the dependency can't be resolved.

### Scenario: Dynamic component loading

The proposed API can be used to greatly simplify dynamic loading of components. It provides a powerful building block to use for implementing custom `AssemblyLoadContext` or event handlers for the binding events like `AppDomain.AssemblyResolve` and `AssemblyLoadContext.Resolving`.

Example of using the new API to load plugins with `AssemblyLoadContext` in isolation:
``` C#
ComponentDependencyResolver resolver = new ComponentDependencyResolver("plugin.dll");

AssemblyLoadContext pluginContext = new AssemblyLoadContext("Plugin");
pluginContext.Resolving += (context, assemblyName) =>
{
    string assemblyPath = resolver.ResolveAssembly(assemblyName);
    if (assemblyPath != null)
    {
        return context.LoadFromAssemblyPath(assemblyPath);
    }
    
    return null;
};

Assembly pluginAssembly = pluginContext.LoadFromAssemblyName(new AssemblyName("Plugin"));

// ... use the pluginAssembly and reflection to invoke functionality from the plugin.
// Dependencies of the plugin are resolved by the event handler above using the resolver
// to provide the actual resolution from assembly name to file path.
```

### Scenario: Inspecting IL metadata of components

Using the newly proposed `MetadataLoadContext` API (see the [proposal](https://github.com/dotnet/corefx/issues/2800)) to inspect IL metadata of components. This API requires an assembly resolver to resolve dependencies of the component. The proposed `ComponentDependencyResolver` would be used to implement such resolver for components produced by the .NET Core SDK.

Example of using the new API to implement `MetadataAssemblyResolver`:
``` C#
public class ComponentMetadataAssemblyResolver : MetadataAssemblyResolver
{
    private ComponentDependencyResolver dependencyResolver;

    public override Assembly Resolve(MetadataLoadContext context, AssemblyName assemblyName)
    {
        string assemblyPath = dependencyResolver.ResolveAssembly(assemblyName);
        if (assemblyPath != null)
        {
            return context.LoadFromAssemblyPath(assemblyPath);
        }

        return null;
    }
}
```


### Context

.NET Core SDK (used by VS, VS Code, VS for Mac and so on) describes component dependencies in the build output via the `.deps.json` files ([description](https://github.com/dotnet/cli/blob/master/Documentation/specs/runtime-configuration-file.md)). These files are consumed by the hosting components (`dotnet.exe` or the app's executable) and they're used to compute the list of dependencies needed to run the application. This happens at startup and through this mechanism all static dependencies of the app are resolved.

Currently there's no such mechanism for components which are loaded dynamically. Applications can use [`Microsoft.Extensions.DependencyModel`](https://github.com/dotnet/core-setup/tree/master/src/managed/Microsoft.Extensions.DependencyModel) package which provides object model of the `.deps.json` file, but it's relatively complex to use this for dependency resolution. It's also very likely that the behavior of such custom solution would be somewhat different from what the hosting layer does for static dependencies.


