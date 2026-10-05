using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.Win32;

namespace AkReporting.WindowsSetup;

internal static class ServiceOperations
{
    public static bool Exists(string name) => ServiceController.GetServices().Any(s => s.ServiceName == name);
    public static async Task Ensure(string name, string command, string installRoot, string? dependency = null)
    {
        if (Exists(name))
        {
            CheckOwned(name, installRoot);
            await Tool("sc.exe", "config", name, "binPath=", command, "start=", "auto");
        }
        else await Tool("sc.exe", "create", name, "binPath=", command, "start=", "auto", "obj=", "NT AUTHORITY\\LocalService");
        await Tool("sc.exe", "sidtype", name, "unrestricted");
        await Tool("sc.exe", "failure", name, "reset=", "86400", "actions=", "restart/5000/restart/15000/restart/60000");
        if (dependency != null) await Tool("sc.exe", "config", name, "depend=", dependency);
    }
    public static void CheckOwned(string name, string installRoot)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name);
        var command = key?.GetValue("ImagePath") as string ?? "";
        if (!command.StartsWith("\"" + Path.GetFullPath(installRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A service with this name belongs to another installation. No changes were made to it.");
    }
    public static void Start(string name)
    {
        using var service = new ServiceController(name);
        service.Refresh();
        if (service.Status != ServiceControllerStatus.Running)
        {
            if (service.Status == ServiceControllerStatus.StopPending) service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
            service.Start(); service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
        }
    }
    public static void Stop(string name)
    {
        if (!Exists(name)) return;
        using var service = new ServiceController(name); service.Refresh();
        if (service.Status == ServiceControllerStatus.Stopped) return;
        if (service.Status != ServiceControllerStatus.StopPending) service.Stop();
        service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
    }
    public static async Task Tool(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start " + Path.GetFileName(executable));
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(true); throw; }
        await stdout; await stderr; // Tool output can contain installation paths; do not log arguments/secrets.
        if (process.ExitCode != 0) throw new InvalidOperationException(Path.GetFileName(executable) + " failed, exit code " + process.ExitCode + ".");
    }
}
