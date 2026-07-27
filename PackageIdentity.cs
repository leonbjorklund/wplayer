using System.Runtime.InteropServices;

namespace WPlayer;

internal static class PackageIdentity
{
    private const int ErrorInsufficientBuffer = 122;

    public static bool IsPackaged
    {
        get
        {
            var length = 0;
            return GetCurrentPackageFullName(ref length, null) == ErrorInsufficientBuffer;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
