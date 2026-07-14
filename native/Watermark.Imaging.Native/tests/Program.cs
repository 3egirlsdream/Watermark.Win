using System;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("__Internal", EntryPoint = "wmi_get_abi_version")]
    private static extern uint GetAbiVersion();

    [DllImport("__Internal", EntryPoint = "wmi_get_capabilities")]
    private static extern uint GetCapabilities();

    private static int Main()
    {
        var abi = GetAbiVersion();
        var capabilities = GetCapabilities();
        Console.WriteLine($"abi={abi} capabilities={capabilities}");
        return abi == 3 && (capabilities & 1) != 0 && (capabilities & (1u << 6)) != 0 ? 0 : 1;
    }
}
