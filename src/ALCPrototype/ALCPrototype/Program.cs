using PluginFramework;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace ALCPrototype
{
    class Program
    {
        static void Main(string[] args)
        {
            // Simple hardcoded way to get to the plugin build output.
            string pluginPath = Path.GetFullPath(Path.Combine(
                Path.GetDirectoryName(Assembly.GetEntryAssembly().Location),
                @"..\..\..\..",
                "PluginOne\\bin\\Debug\\netcoreapp2.1\\PluginOne.dll"));

            Console.WriteLine("Full isolation context:");
            try
            {
                ComponentDependencyResolver resolver = new ComponentDependencyResolver(pluginPath);
                AssemblyLoadContext_ fullIsolationContext = new AssemblyLoadContext_("FullIsolation");
                fullIsolationContext.Loading += resolver.LoadAssembly;
                fullIsolationContext.LoadingUnmanagedDll += resolver.LoadUnmanagedDll;

                Assembly asm = fullIsolationContext.LoadFromAssemblyName(new AssemblyName("PluginOne"));

                Console.WriteLine(((IPlugin)asm.CreateInstance("PluginOne.PluginOne")).GetDescription());
            }
            catch (InvalidCastException)
            {
                Console.WriteLine("Full isolation failed as expected.");
            }

            Console.WriteLine();
            Console.WriteLine("Full isolation context with explicit sharing:");
            {
                ComponentDependencyResolver resolver = new ComponentDependencyResolver(pluginPath);
                AssemblyLoadContext_ fullIsolationContext = new AssemblyLoadContext_("FullIsolation");
                fullIsolationContext.Loading += (context, asmName) =>
                {
                    if (typeof(IPlugin).Assembly.GetName().Name == asmName.Name)
                    {
                        // Explicitly share the IPlugin
                        return null;
                    }

                    return resolver.LoadAssembly(context, asmName);
                };
                fullIsolationContext.LoadingUnmanagedDll += resolver.LoadUnmanagedDll;

                Assembly asm = fullIsolationContext.LoadFromAssemblyName(new AssemblyName("PluginOne"));

                Console.WriteLine(((IPlugin)asm.CreateInstance("PluginOne.PluginOne")).GetDescription());
            }

            Console.WriteLine();
            Console.WriteLine("Prefer Default context:");
            {
                ComponentDependencyResolver resolver = new ComponentDependencyResolver(pluginPath);
                AssemblyLoadContext_ preferDefaultContext = new AssemblyLoadContext_("PreferDefault");
                preferDefaultContext.Resolving += resolver.LoadAssembly;
                preferDefaultContext.LoadingUnmanagedDll += resolver.LoadUnmanagedDll;

                Assembly asm = preferDefaultContext.LoadFromAssemblyName(new AssemblyName("PluginOne"));

                Console.WriteLine(((IPlugin)asm.CreateInstance("PluginOne.PluginOne")).GetDescription());
            }

            // (must run last as it pollutes Default)
            Console.WriteLine();
            Console.WriteLine("Load into Default:");
            {
                ComponentDependencyResolver resolver = new ComponentDependencyResolver(pluginPath);
                AssemblyLoadContext.Default.Resolving += resolver.LoadAssembly;
                // No way to hook unmanaged dll loads on default

                Assembly asm = AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName("PluginOne"));

                Console.WriteLine(((IPlugin)asm.CreateInstance("PluginOne.PluginOne")).GetDescription());
            }
        }
    }
}
