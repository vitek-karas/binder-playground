using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace ALCPrototype
{
    public class ComponentDependencyResolver
    {
        private string _componentMainAssemblyPath;

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
                }
            }
        }

        public string ResolveAssembly(AssemblyName assemblyName)
        {
            return null;
        }

        public string ResolveUnmanagedDll(string unmanagedDllName)
        {
            return null;
        }
    }
}
