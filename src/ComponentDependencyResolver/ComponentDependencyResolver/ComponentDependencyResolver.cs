using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace System.Runtime.Loader
{
    /// <summary>
    /// Provides resolution of assembly and native library dependecies for a componet.
    /// </summary>
    public class ComponentDependencyResolver
    {
        private string _componentAssemblyPath;
        private Dictionary<string, string> _assemblyNameToPathMap;
        private string[] _nativeSearchPaths;
        private string[] _resourceSearchPaths;

        public ComponentDependencyResolver(string componentAssemblyPath)
        {
            _componentAssemblyPath = componentAssemblyPath ?? throw new ArgumentNullException(componentAssemblyPath);

            string assemblyPaths = null, nativeSearchPaths = null, resourceSearchPaths = null;
            int hr = HostPolicy.corehost_resolve_component_dependencies(
                _componentAssemblyPath,
                (assembly_paths, native_search_paths, resource_search_paths) =>
                {
                    assemblyPaths = assembly_paths;
                    nativeSearchPaths = native_search_paths;
                    resourceSearchPaths = resource_search_paths;
                });

            if (hr != 0)
            {
                throw new Exception($"Failed to prepare dependency resolution for component '{_componentAssemblyPath}'.");
            }

            try
            {
                // TODO: Are assembly names case sensitive?
                _assemblyNameToPathMap = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string assemblyPath in assemblyPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    string simpleName = Path.GetFileNameWithoutExtension(assemblyPath);
                    _assemblyNameToPathMap.Add(simpleName, assemblyPath);
                }

                _nativeSearchPaths = nativeSearchPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                _resourceSearchPaths = resourceSearchPaths.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            }
            catch (Exception innerException)
            {
                throw new Exception($"Failed to prepare dependency resolution for component '{_componentAssemblyPath}'.", innerException);
            }
        }

        public string ResolveAssembly(AssemblyName assemblyName)
        {
            if (!string.IsNullOrEmpty(assemblyName.CultureName))
            {
                // Find the assembly in resource search paths
                // TODO: This will have to emulate the runtime binder behavior regarding satellites resolution
                foreach (string resourceSearchPath in _resourceSearchPaths)
                {
                    string candidatePath = Path.Combine(resourceSearchPath, assemblyName.CultureName, assemblyName.Name + ".dll");
                    if (File.Exists(candidatePath))
                    {
                        return candidatePath;
                    }
                }

                return null;
            }

            if (_assemblyNameToPathMap.TryGetValue(assemblyName.Name, out string assemblyPath))
            {
                return assemblyPath;
            }

            return null;
        }

        public string ResolveUnmanagedDll(string name)
        {
            // TODO: This will have to emulate runtime native library probing
            foreach (string nativeSearchPath in _nativeSearchPaths)
            {
                string candidatePath = Path.Combine(nativeSearchPath, name + ".dll");
                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }
    }
}
