using AkReporting.Deployment;

namespace AkReporting.WindowsSetup;

internal sealed class SetupForm : Form
{
    private readonly TextBox admin = new();
    private readonly TextBox adminPassword = new() { UseSystemPasswordChar = true };
    private readonly TextBox writer = new();
    private readonly TextBox writerPassword = new() { UseSystemPasswordChar = true };
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(530, 0) };
    private readonly FlowLayoutPanel buttons = new() { AutoSize = true };
    private readonly string root;
    private bool working;
    private byte[]? logoPng;
    public SetupForm(string installRoot)
    {
        root = installRoot; Text = "A K Reporting — Local Windows Setup"; Width = 640; Height = 760;
        StartPosition = FormStartPosition.CenterScreen; Font = new Font("Segoe UI", 10); MinimumSize = new Size(620, 650);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, AutoScroll = true };
        Controls.Add(layout);
        void Row(Control control) { control.Margin = new Padding(0, 5, 0, 5); control.Width = 530; layout.Controls.Add(control); }
        Row(new Label { Text = "Configure this Windows 10/11 PC", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        Row(new Label { Text = "Installs local database/API services and trusted localhost HTTPS. Reports remain drafts until center medical review. Choose your own account passwords (12–256 characters).", AutoSize = true, MaximumSize = new Size(530, 0) });
        Row(new Label { Text = "Administrator username", AutoSize = true }); Row(admin);
        Row(new Label { Text = "Administrator password", AutoSize = true }); Row(adminPassword);
        Row(new Label { Text = "Report-writer username (separate account)", AutoSize = true }); Row(writer);
        Row(new Label { Text = "Report-writer password", AutoSize = true }); Row(writerPassword);
        var logoStatus = new Label { Text = "Centre logo (optional). Used on startup and login; it does not change report letterhead.", AutoSize = true, MaximumSize = new Size(530, 0) };
        var logo = new Button { Text = "Upload your AK logo…", AutoSize = true };
        logo.Click += (_, _) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Logo image|*.png;*.jpg;*.jpeg", Multiselect = false };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024) throw new InvalidOperationException("Choose a logo smaller than 2 MiB.");
                using var source = Image.FromFile(dialog.FileName);
                if (source.Width > 4096 || source.Height > 4096) throw new InvalidOperationException("Logo must not exceed 4096 × 4096 pixels.");
                using var encoded = new MemoryStream(); source.Save(encoded, System.Drawing.Imaging.ImageFormat.Png);
                var bytes = encoded.ToArray(); Branding.Validate(bytes); logoPng = bytes;
                logoStatus.Text = "Logo selected: " + Path.GetFileName(dialog.FileName) + ". It will appear after setup completes.";
            }
            catch (Exception error) { MessageBox.Show(this, error is InvalidOperationException ? error.Message : "The logo could not be loaded. Choose a valid PNG or JPEG image.", "Centre logo"); }
        };
        Row(logoStatus); Row(logo);
        var initialize = new Button { Text = "Initialize / resume setup", AutoSize = true };
        var repair = new Button { Text = "Repair existing setup", AutoSize = true };
        initialize.Click += async (_, _) => await Run(new InitialAccounts { AdministratorName = admin.Text.Trim(), AdministratorPassword = adminPassword.Text,
            WriterName = writer.Text.Trim(), WriterPassword = writerPassword.Text, LogoPng = logoPng });
        repair.Click += async (_, _) => await Run(new InitialAccounts { LogoPng = logoPng });
        buttons.Controls.Add(initialize); buttons.Controls.Add(repair); Row(buttons); Row(status);
        Row(new Label { Text = "When Ready appears, close setup and open the A K Reporting desktop shortcut as your normal Windows user. Administrators can prepare reports and manage doctors.\nData location: " + InstallationPaths.DataRoot + "\nAPI: https://localhost:7043\nUninstall offers Keep data or Remove everything.", AutoSize = true, MaximumSize = new Size(530, 0) });
        FormClosing += (_, e) => { if (working) { e.Cancel = true; status.Text = "Wait for the current setup operation to finish."; } };
    }
    private async Task Run(InitialAccounts accounts)
    {
        if (working) return; working = true; buttons.Enabled = false;
        var provisioner = new Provisioner(root, message => BeginInvoke((Action)(() => status.Text = message)));
        try { await Task.Run(() => provisioner.Initialize(accounts)); }
        catch (Exception error)
        {
            provisioner.RecordFailure(error);
            status.Text = error is InvalidOperationException or AkReporting.Domain.ValidationException ? error.Message :
                "Setup failed (" + error.GetType().Name + "). Check services/ports and the protected setup-status.log. Re-run to resume; existing reports are preserved.";
        }
        finally { adminPassword.Clear(); writerPassword.Clear(); working = false; buttons.Enabled = true; }
    }
}
