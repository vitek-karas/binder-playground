using PluginFramework;

namespace PluginOne
{
    public class PluginOne : IPlugin
    {
        public string GetDescription()
        {
            return $"{nameof(PluginOne)} in {this.GetType().Assembly.GetName().ToString()} from {this.GetType().Assembly.Location}";
        }
    }
}
