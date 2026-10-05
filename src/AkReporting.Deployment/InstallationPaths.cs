namespace AkReporting.Deployment;

public static class InstallationPaths
{
    public const string HostService = "AKReportingHost";
    public const string DatabaseService = "AKReportingDatabase";
    public const string ProductFolder = "AK Diagnostic Reporting";
    public const int DatabasePort = 55432;
    public const int HttpsPort = 7043;
    public static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductFolder);
    public static string HostSettings(string root) => Path.Combine(root, "host", "settings.dpapi");
    public static string OwnerSettings(string root) => Path.Combine(root, "administration", "owner.dpapi");
}
