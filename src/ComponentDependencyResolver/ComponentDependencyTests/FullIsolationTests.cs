using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using Xunit;
using Xunit.Abstractions;

namespace ComponentDependencyTests
{
    public class FullIsolationTests
    {
        public class FullIsolationLoadContext : AssemblyLoadContext
        {
            private ComponentDependencyResolver _resolver;

            public FullIsolationLoadContext(string componentAssemblyPath)
            {
                _resolver = new ComponentDependencyResolver(componentAssemblyPath);
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                string assemblyPath = _resolver.ResolveAssembly(assemblyName);
                if (assemblyPath != null)
                {
                    return this.LoadFromAssemblyPath(assemblyPath);
                }
                else
                {
                    return null;
                }
            }

            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                string unmanagedDllPath = _resolver.ResolveUnmanagedDll(unmanagedDllName);
                if (unmanagedDllPath != null)
                {
                    return this.LoadUnmanagedDllFromPath(unmanagedDllPath);
                }
                else
                {
                    return IntPtr.Zero;
                }
            }
        }

        private string _basePath;
        private ITestOutputHelper _output;

        public FullIsolationTests(ITestOutputHelper output)
        {
            _basePath = Path.GetDirectoryName(this.GetType().Assembly.Location);
            _output = output;
        }

        [Fact]
        public void ComponentWithNoDependencies()
        {
            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                _output.WriteLine(m.FileName);
            }

            FullIsolationLoadContext loadContext = new FullIsolationLoadContext(GetStandardComponentLocation("ComponentWithNoDependencies"));

            Assembly assembly = loadContext.LoadFromAssemblyName(new AssemblyName("ComponentWithNoDependencies"));
            OutputComponentDescription(assembly);
        }

        private string GetStandardComponentLocation(string componentName)
        {
            return Path.Combine(_basePath, componentName, "bin", "Debug", "netcoreapp2.1");
        }

        private void OutputComponentDescription(Assembly assembly)
        {
            string componentName = assembly.GetName().Name;
            object componentInstance = assembly.CreateInstance($"{componentName}.{componentName}");
            _output.WriteLine((string)componentInstance.GetType().GetMethod("GetComponentDescription").Invoke(componentInstance, new object[0]));
        }
    }
}
