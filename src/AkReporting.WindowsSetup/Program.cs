using System.Text.Json;

namespace AkReporting.WindowsSetup;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var root = Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName;
        var rootIndex = Array.IndexOf(args, "--install-root");
        if (rootIndex >= 0 && rootIndex + 1 < args.Length) root = args[rootIndex + 1];
        if (args.Contains("--initialize-stdin") || args.Contains("--repair") || args.Contains("--stop-services") || args.Contains("--remove-services") || args.Contains("--purge-data"))
        {
            var provisioner = new Provisioner(root, Console.WriteLine);
            try
            {
                if (args.Contains("--purge-data")) provisioner.Purge().GetAwaiter().GetResult();
                else if (args.Contains("--stop-services") || args.Contains("--remove-services")) provisioner.Stop(args.Contains("--remove-services")).GetAwaiter().GetResult();
                else
                {
                    var request = args.Contains("--initialize-stdin") ? JsonSerializer.Deserialize<InitialAccounts>(Console.In.ReadToEnd())
                        ?? throw new InvalidOperationException("Invalid initial account payload.") : new InitialAccounts();
                    provisioner.Initialize(request).GetAwaiter().GetResult();
                }
                return 0;
            }
            catch (Exception error)
            {
                provisioner.RecordFailure(error);
                Console.Error.WriteLine("Setup failed: " + error.GetType().Name + ". See protected setup-status.log and the installation troubleshooting guide.");
                return 1;
            }
        }
        ApplicationConfiguration.Initialize();
        System.Windows.Forms.Application.Run(new SetupForm(root));
        return 0;
    }
}
