using System.Diagnostics;

namespace UwpPipe.Common;

public static class CommonValues
{
    public const string PipeName = "Baka632-Pipe";

    public static string GetPipeNameForPackagedApps(string packageSid)
    {
        int sessionId = Process.GetCurrentProcess().SessionId;
        return $@"Sessions\{sessionId}\AppContainerNamedObjects\{packageSid}\Baka632-Pipe";
    }
}
