param(
    [Parameter(Mandatory = $true)] [string] $Arguments,
    [string] $AppUserModelId = 'BlenderFoundation.Blender4.5LTS_ppwjx1n5r4v9t!BLENDER'
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IApplicationActivationManager
{
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
        [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    int ActivateForFile(string appUserModelId, IntPtr itemArray, string verb, out uint processId);
    int ActivateForProtocol(string appUserModelId, IntPtr itemArray, out uint processId);
}

[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
class ApplicationActivationManager { }

public static class PackagedAppLauncher
{
    public static uint Launch(string appUserModelId, string arguments)
    {
        var manager = (IApplicationActivationManager)new ApplicationActivationManager();
        uint processId;
        int result = manager.ActivateApplication(appUserModelId, arguments, 0, out processId);
        if (result != 0) Marshal.ThrowExceptionForHR(result);
        return processId;
    }
}
'@

$processId = [PackagedAppLauncher]::Launch($AppUserModelId, $Arguments)
Write-Output $processId
Wait-Process -Id $processId
