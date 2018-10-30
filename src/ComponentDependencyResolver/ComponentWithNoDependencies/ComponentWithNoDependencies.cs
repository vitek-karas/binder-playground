using System;
using System.Reflection;
using System.Runtime.Loader;

namespace ComponentWithNoDependencies
{
    public class ComponentWithNoDependencies
    {
        public string GetComponentDescription()
        {
            Assembly assembly = this.GetType().Assembly;
            return $"{this.GetType().Name} from {assembly.FullName}{Environment.NewLine}" +
                $"Location: {assembly.Location}{Environment.NewLine}" +
                $"Load context: {AssemblyLoadContext.GetLoadContext(assembly).ToString()}";
        }
    }
}
