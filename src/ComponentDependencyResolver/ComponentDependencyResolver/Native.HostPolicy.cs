using System.Runtime.InteropServices;

namespace System.Runtime.Loader
{
    internal static class HostPolicy
    {
#if WINDOWS
        private const CharSet OSCharSet = CharSet.Unicode;
#else
        private const CharSet OSCharSet = CharSet.Ansi; // actually UTF8 on Unix
#endif

        [UnmanagedFunctionPointer(CallingConvention.Cdecl, CharSet = OSCharSet)]
        internal delegate void corehost_resolve_component_dependencies_result_fn(
            string assembly_paths,
            string native_search_paths,
            string resource_search_paths);

        [DllImport("hostpolicy", CharSet = OSCharSet, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern int corehost_resolve_component_dependencies(
            string component_main_assembly_path,
            corehost_resolve_component_dependencies_result_fn result);
    }
}
