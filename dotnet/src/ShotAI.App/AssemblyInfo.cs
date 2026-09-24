using System.Runtime.InteropServices;

// INV-PKG-16 (spec 12 7.9.1): a P/Invoke that names no search path of its own looks only in
// System32 and this assembly's folder, never the current folder or PATH.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32 | DllImportSearchPath.AssemblyDirectory)]
