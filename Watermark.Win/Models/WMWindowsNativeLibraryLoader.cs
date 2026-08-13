using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using Watermark.Shared.Models;

namespace Watermark.Win.Models;

internal static class WMWindowsNativeLibraryLoader
{
    private const string LibraryName = "Watermark.Imaging.Native";
    private const string LibraryFileName = $"{LibraryName}.dll";
    private static readonly object Gate = new();
    private static IntPtr libraryHandle;
    private static bool registered;

    public static void Register()
    {
        lock (Gate)
        {
            if (registered) return;
            registered = true;
            var sharedAssembly = typeof(WMOcioColorEngine).Assembly;
            try
            {
                NativeLibrary.SetDllImportResolver(sharedAssembly, ResolveLibrary);
            }
            catch (InvalidOperationException)
            {
                // Another shared imaging entry point registered the same resolver first.
            }

            _ = EnsureLoaded();
        }
    }

    private static IntPtr ResolveLibrary(
        string libraryName,
        Assembly assembly,
        DllImportSearchPath? searchPath) =>
        string.Equals(libraryName, LibraryName, StringComparison.Ordinal)
            ? EnsureLoaded()
            : IntPtr.Zero;

    private static IntPtr EnsureLoaded()
    {
        lock (Gate)
        {
            if (libraryHandle != IntPtr.Zero) return libraryHandle;
            foreach (var packagedPath in PackagedPaths())
            {
                if (File.Exists(packagedPath) && NativeLibrary.TryLoad(packagedPath, out libraryHandle))
                    return libraryHandle;
            }
            return NativeLibrary.TryLoad(LibraryName, out libraryHandle) ? libraryHandle : IntPtr.Zero;
        }
    }

    private static IEnumerable<string> PackagedPaths()
    {
        var runtimeIdentifier = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null
        };
        if (runtimeIdentifier is not null)
        {
            yield return Path.Combine(
                AppContext.BaseDirectory,
                "runtimes",
                runtimeIdentifier,
                "native",
                LibraryFileName);
        }

        // Retain compatibility with older packages that placed the DLL beside the executable.
        yield return Path.Combine(AppContext.BaseDirectory, LibraryFileName);
    }
}
