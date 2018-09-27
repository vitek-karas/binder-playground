using System;
using System.Reflection;
using System.Runtime.Loader;

namespace ALCPrototype
{
    public class FullIsolationContext : AssemblyLoadContext
    {
        private ComponentDependencyResolver _resolver;

        public FullIsolationContext(string componentMainAssemblyPath)
        {
            _resolver = new ComponentDependencyResolver(componentMainAssemblyPath);
        }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            string resolvedPath = _resolver.ResolveAssembly(assemblyName);
            if (resolvedPath != null)
            {
                return LoadFromAssemblyPath(resolvedPath);
            }

            return null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string resolvedPath = _resolver.ResolveUnmanagedDll(unmanagedDllName);
            if (resolvedPath != null)
            {
                return LoadUnmanagedDllFromPath(resolvedPath);
            }

            return base.LoadUnmanagedDll(unmanagedDllName);
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
        }
    }
}
