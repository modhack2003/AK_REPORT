using System;
using System.Windows;

namespace AkReporting.Desktop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (_, error) =>
            {
                MessageBox.Show("The operation could not complete. Restart the application and reload the saved report. Unsaved entries may need to be re-entered.", "A K Diagnostic Reporting");
                error.Handled = true;
            };
        }
    }
}
