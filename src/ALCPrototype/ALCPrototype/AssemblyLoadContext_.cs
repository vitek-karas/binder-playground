﻿using System;
using System.Reflection;
using System.Runtime.Loader;

namespace ALCPrototype
{
    /// <summary>
    /// This simulates the changes we could do to AssemblyLoadContext to make it easier to consume without subclassing.
    /// </summary>
    public class AssemblyLoadContext_ : AssemblyLoadContext
    {
        /// <summary>
        /// This would have to be disabled for Default load context (to avoid recursive binding).
        /// </summary>
        public event Func<AssemblyLoadContext, AssemblyName, Assembly> Loading;

        /// <summary>
        /// Similarly this might need to be disabled for Default load context.
        /// </summary>
        public event Func<AssemblyLoadContext, string, IntPtr> LoadingUnmanagedDll;

        /// <summary>
        /// ALC now has an optional name - to provide good ToString();
        /// </summary>
        public string Name { get; set; }

        public AssemblyLoadContext_() { }
        public AssemblyLoadContext_(string name) { Name = name;  }

        protected override Assembly Load(AssemblyName assemblyName)
        {
            return Loading?.Invoke(this, assemblyName);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            return LoadingUnmanagedDll?.Invoke(this, unmanagedDllName) ?? IntPtr.Zero;
        }

        public override string ToString()
        {
            return (Name ?? "AssemblyLoadContext") + " " + GetHashCode().ToString();
        }
    }
}
