using System.Security.Principal;
using Windows.Win32;
using Windows.Win32.Security;

namespace UwpPipe.Common;

public static class CommonValues
{
    public const string PipeName = "Baka632-Pipe";

    public static string? TryGetPackageSid(string? packageFamilyName)
    {
        if (!string.IsNullOrWhiteSpace(packageFamilyName)
            && PInvoke.DeriveAppContainerSidFromAppContainerName(packageFamilyName, out PSID psid).Succeeded)
        {
            string packageSid;
            unsafe
            {
                SecurityIdentifier appSid = new((nint)psid.Value);
                packageSid = appSid.ToString();
                PInvoke.FreeSid(psid);
            }
            return packageSid;
        }
        else
        {
            return null;
        }
    }

    public static string GetPipeNameForPackagedApps(int sessionId, string packageSid, string pipeName)
    {
        return $@"Sessions\{sessionId}\AppContainerNamedObjects\{packageSid}\{pipeName}";
    }

    public static int? GetCurrentSessionIdNative()
    {
        uint pid = PInvoke.GetCurrentProcessId();

        return PInvoke.ProcessIdToSessionId(pid, out uint sessionId)
            ? (int)sessionId
            : null;
    }
}
