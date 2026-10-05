using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using AkReporting.Contracts;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace AkReporting.Desktop
{
    public sealed class CatalogPanel : StackPanel
    {
        private ApiClient api = null!;
        private Action<string> status = null!;
        private DoctorLibraryPanel? library;
        public event EventHandler? LibraryChanged;
        public void ClearProtectedState()
        {
            library?.ClearProtectedState(); library = null; Children.Clear(); api = null!; status = null!;
        }
        public async Task Connect(ApiClient client, Action<string> notify)
        {
            api = client; status = notify; Children.Clear();
            if (api.Role == "Administrator") AdminControls();
            if (api.Role == "MedicalReviewer") ReviewControls();
            if (api.Role != "Administrator" && api.Role != "MedicalReviewer")
                Children.Add(new TextBlock { Text = "Catalog changes require the administrator or qualified reviewer role." });
            if (library != null) await library.Refresh();
        }
        private void AdminControls()
        {
            library = new DoctorLibraryPanel(api, status); library.LibraryChanged += (_, __) => LibraryChanged?.Invoke(this, EventArgs.Empty);
            Children.Add(library);
            var advanced = new StackPanel();
            var details = new Expander { Header = "Users, configuration and centre settings", Content = advanced, IsExpanded = false };
            Children.Add(details);
            // Build the less frequent administrative controls inside a collapsed group.
            var start = Children.Count;
            Title("User provisioning");
            var username = Field("Username"); var password = new PasswordBox { Padding = new Thickness(6) }; Children.Add(password);
            var role = new ComboBox { ItemsSource = new[] { "Writer", "Receptionist", "MedicalReviewer", "Administrator" }, SelectedIndex = 0 }; Children.Add(role);
            Button("Create user", async () => { await api.Post<object>("users", new CreateUserRequest { Username = username.Text, Password = password.Password, Role = (string)role.SelectedItem }); password.Clear(); status("User created."); });
            Title("Versioned templates");
            Children.Add(new TextBlock { Text = "Draft imports are developer-maintained definitions. Medical approval is a separate qualified-reviewer action." });
            Button("Install five candidate draft schemas", async () => { await api.Post<object?>("templates/seed-candidates", new { }); status("Five draft schemas installed. Choose New report to begin."); });
            Button("Import draft template JSON", async () =>
            {
                var dialog = new OpenFileDialog { Filter = "Template JSON|*.json", Multiselect = false };
                if (dialog.ShowDialog() != true) return;
                if (new FileInfo(dialog.FileName).Length > 256 * 1024) throw new ArgumentException("Template definition exceeds 256 KiB.");
                var template = JsonConvert.DeserializeObject<TemplateDefinition>(File.ReadAllText(dialog.FileName), new JsonSerializerSettings { MaxDepth = 32 }) ?? throw new ArgumentException("Invalid template definition.");
                await api.Post<TemplateDefinition>("templates", template); status("New draft template version stored.");
            });
            Title("Center settings"); var center = Field("Center display name"); var prefix = Field("Report-number prefix"); var days = Field("Retention days (state-based, no automatic deletion)");
            Button("Load settings", async () => { var settings = await api.Get<CenterSettings>("settings"); center.Text = settings.CenterName; prefix.Text = settings.ReportPrefix; days.Text = settings.RetentionDays.ToString(CultureInfo.InvariantCulture); });
            Button("Save settings", async () => { await api.Post<object?>("settings", new CenterSettings { CenterName = center.Text, ReportPrefix = prefix.Text, RetentionDays = int.Parse(days.Text, CultureInfo.InvariantCulture) }); status("Settings stored; existing report IDs and template offsets remain pinned."); });
            while (Children.Count > start) { var child = Children[start]; Children.RemoveAt(start); advanced.Children.Add(child); }
        }
        private void ReviewControls()
        {
            Title("Qualified medical configuration review");
            Children.Add(new TextBlock { Text = "Record completed center/SOP/source review. The application does not establish reviewer credentials or perform medical validation." });
            var id = Field("Draft template-version UUID"); var reviewer = Field("Actual qualified reviewer name"); var evidence = Field("Dated source/SOP/center comparison and approval evidence");
            Button("Publish new approved version", async () =>
            {
                var approved = await api.Post<TemplateDefinition>("templates/" + Guid.Parse(id.Text) + "/approve", new ApprovalRequest { ReviewerName = reviewer.Text, Evidence = evidence.Text });
                status("Approved immutable version " + approved.Version + ": " + approved.VersionId);
            });
        }
        private void Title(string text) => Children.Add(new TextBlock { Text = text, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 18, 0, 8) });
        private TextBox Field(string label)
        {
            Children.Add(new TextBlock { Text = label }); var field = new TextBox { MaxLength = 8000 }; Children.Add(field); return field;
        }
        private void Button(string caption, Func<Task> action)
        {
            var button = new Button { Content = caption, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += async (_, __) =>
            {
                IsEnabled = false;
                try { await action(); }
                catch (Exception e) { status(e is ApiException || e is ArgumentException || e is FormatException ? e.Message : "Catalog operation failed. Check the application host and protected configuration."); }
                finally { IsEnabled = true; }
            };
            Children.Add(button);
        }
    }
}
