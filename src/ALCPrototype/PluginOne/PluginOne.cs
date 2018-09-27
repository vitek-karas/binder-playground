using PluginFramework;
using System.Runtime.Loader;

namespace PluginOne
{
    public class PluginOne : IPlugin
    {
        public string GetDescription()
        {
            return $"{this.GetType().Name} \r\n" +
                $"in {this.GetType().Assembly.GetName().ToString()} {this.GetType().Assembly.GetHashCode().ToString()} \r\n" +
                $"from {this.GetType().Assembly.Location} \r\n" +
                $"Context {AssemblyLoadContext.GetLoadContext(this.GetType().Assembly).ToString()}";
        }
    }
}
