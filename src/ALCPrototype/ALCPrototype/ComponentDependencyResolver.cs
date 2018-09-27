using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace ALCPrototype
{
    /// <summary>
    /// Dependency resolver - standalone class which provides the .deps.json based asset resolution.
    /// 
    /// For now implements simplistic resolution of normal assemblies.
    /// 
    /// HACKS:
    /// - classlib doesn't produce .runtimeconfig.dev.json. So unpublished classlib is not self-describing
    ///   as there's no way to tell the probing paths to use (mainly NuGet paths).
    ///   Hack is to use .runtimeconfig.dev.json from the main exe.
    /// - AssemblyLoadContext.LoadUnmanagedDllFromPath is protected, can't be called from the outside.
    ///   It's "funny" since that method has no reason to be instance method to beging with
    ///   it might as well existing on its own as static method on any class (there are no specific ties to ALC).
    ///   Hack is to use reflection to overcome the visibility.
    /// 
    /// </summary>
    public class ComponentDependencyResolver
    {
        private string _componentMainAssemblyPath;
        private List<string> _probingPaths = new List<string>();

        public ComponentDependencyResolver(string componentMainAssemblyPath)
        {
            _componentMainAssemblyPath = componentMainAssemblyPath;

            string depsJsonPath =
                Path.Combine(
                    Path.GetDirectoryName(_componentMainAssemblyPath),
                    Path.GetFileNameWithoutExtension(_componentMainAssemblyPath) + ".deps.json");

            if (File.Exists(depsJsonPath))
            {
                using (JsonReader reader = new JsonTextReader(new StreamReader(depsJsonPath)))
                {
                    JObject root = JObject.Load(reader);
                    LoadDepsJson(root);
                }
            }

            _probingPaths.Add(Path.GetDirectoryName(_componentMainAssemblyPath));
            LoadRuntimeConfig();
        }

        public string ResolveAssembly(AssemblyName assemblyName)
        {
            if (string.IsNullOrEmpty(assemblyName.CultureName))
            {
                if (_runtimeAssets.TryGetValue(assemblyName.Name, out RuntimeAsset runtimeAsset))
                {
                    string assetPath = ResolveAssetPath(runtimeAsset);
                    if (assetPath != null)
                    {
                        return assetPath;
                    }
                }
            }

            return null;
        }

        public Assembly LoadAssembly(AssemblyLoadContext loadContext, AssemblyName assemblyName)
        {
            string path = ResolveAssembly(assemblyName);
            if (path != null)
            {
                return loadContext.LoadFromAssemblyPath(path);
            }

            return null;
        }

        public string ResolveUnmanagedDll(string unmanagedDllName)
        {
            return null;
        }

        public IntPtr LoadUnmanagedDll(AssemblyLoadContext loadContext, string unmanagedDllName)
        {
            string path = ResolveUnmanagedDll(unmanagedDllName);
            if (path != null)
            {
                // Use reflection to overcome visibility
                return (IntPtr)loadContext.GetType()
                    .GetMethod("LoadUnmanagedDllFromPath", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(loadContext, new object[] { path });
            }

            return IntPtr.Zero;
        }

        private string ResolveAssetPath(Asset asset)
        {
            foreach (string probingPath in _probingPaths)
            {
                string candidatePath = probingPath;
                if (asset.LibraryRelativePath != null)
                {
                    candidatePath = Path.Combine(candidatePath, asset.LibraryRelativePath);
                }

                candidatePath = Path.Combine(candidatePath, asset.RelativePath);

                if (File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            return null;
        }

        private class Asset
        {
            public string LibraryName { get; set; }

            public string RelativePath { get; set; }

            public string LibraryRelativePath { get; set; }
        }

        private class RuntimeAsset : Asset
        {
            public string SimpleName { get; set; }
        }

        Dictionary<string, RuntimeAsset> _runtimeAssets = new Dictionary<string, RuntimeAsset>(StringComparer.Ordinal);

        private void LoadDepsJson(JObject root)
        {
            List<Asset> assets = new List<Asset>(); ;

            JObject target = (JObject)root["targets"][(string)root["runtimeTarget"]["name"]];
            foreach (JProperty dependency in target.Properties())
            {
                foreach (JProperty runtimeAsset in ((JObject)dependency.Value["runtime"]).Properties())
                {
                    RuntimeAsset asset = new RuntimeAsset()
                    {
                        LibraryName = dependency.Name,
                        RelativePath = runtimeAsset.Name,
                        SimpleName = Path.GetFileNameWithoutExtension(runtimeAsset.Name)
                    };

                    _runtimeAssets.Add(asset.SimpleName, asset);
                    assets.Add(asset);
                }
            }

            foreach (JProperty library in ((JObject)root["libraries"]).Properties())
            {
                string libraryName = library.Name;
                string libraryRelativePath = (string)library.Value["path"];
                if (libraryRelativePath != null)
                {
                    foreach (Asset asset in assets
                        .Where(ra => ra.LibraryName.Equals(libraryName, StringComparison.OrdinalIgnoreCase)))
                    {
                        asset.LibraryRelativePath = libraryRelativePath;
                    }
                }
            }
        }

        private void LoadRuntimeConfig()
        {
            Assembly mainAssembly = Assembly.GetEntryAssembly();

            string candidateRuntimeConfig = Path.Combine(
                Path.GetDirectoryName(mainAssembly.Location),
                Path.GetFileNameWithoutExtension(mainAssembly.Location) + ".runtimeconfig.json");
            LoadRuntimeConfig(candidateRuntimeConfig);

            candidateRuntimeConfig = Path.Combine(
                Path.GetDirectoryName(mainAssembly.Location),
                Path.GetFileNameWithoutExtension(mainAssembly.Location) + ".runtimeconfig.dev.json");
            LoadRuntimeConfig(candidateRuntimeConfig);
        }

        private void LoadRuntimeConfig(string path)
        {
            if (File.Exists(path))
            {
                using (JsonReader reader = new JsonTextReader(new StreamReader(path)))
                {
                    JObject root = JObject.Load(reader);

                    JArray probingPaths = (JArray)root["runtimeOptions"]["additionaProbingPaths"];
                    if (probingPaths != null)
                    {
                        foreach (JValue value in probingPaths)
                        {
                            _probingPaths.Add((string)value);
                        }
                    }
                }
            }
        }
    }
}
