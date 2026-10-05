using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AkReporting.Contracts;
using Microsoft.Win32;

namespace AkReporting.Desktop
{
    public sealed class DoctorLibraryPanel : Grid
    {
        private readonly ApiClient api;
        private readonly Action<string> notify;
        private readonly ListBox list = new ListBox { DisplayMemberPath = "DisplayName", MinHeight = 250 };
        private readonly Dictionary<string, TextBox> fields = new Dictionary<string, TextBox>();
        private readonly Image signaturePreview = new Image { Height = 80, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly Image stampPreview = new Image { Height = 80, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
        private readonly TextBlock assetStatus = new TextBlock();
        private readonly TextBlock heading = new TextBlock { FontSize = 20, FontWeight = FontWeights.SemiBold };
        private DoctorVersion? selected;
        private byte[]? signature;
        private byte[]? stamp;
        private bool loading;
        public event EventHandler? LibraryChanged;

        public DoctorLibraryPanel(ApiClient client, Action<string> status)
        {
            api = client; notify = status;
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) }); ColumnDefinitions.Add(new ColumnDefinition());
            var sidebar = new StackPanel { Margin = new Thickness(0, 0, 24, 0) }; Children.Add(sidebar);
            sidebar.Children.Add(new TextBlock { Text = "Doctor library", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) });
            sidebar.Children.Add(Action("+ Add doctor", () => { NewDoctor(); return Task.CompletedTask; }));
            sidebar.Children.Add(Action("Refresh library", Refresh)); sidebar.Children.Add(list);
            var form = new StackPanel(); Grid.SetColumn(form, 1); Children.Add(form); form.Children.Add(heading);
            form.Children.Add(new TextBlock { Text = "Add actual centre-supplied details. Updating creates a new version; previously saved reports keep their original doctor/signature.", Margin = new Thickness(0, 8, 0, 16), Foreground = Brushes.DimGray });
            Field(form, "name", "Doctor name *", 200); Field(form, "display", "Name shown in the report (blank uses doctor name)", 200);
            Field(form, "qualification", "Qualification (optional)", 300); Field(form, "designation", "Designation (optional)", 200);
            Field(form, "registration", "Registration number (optional)", 100); Field(form, "specialty", "Specialty (optional)", 200);
            Field(form, "evidence", "Permission to use details / signature / stamp *", 4000);
            form.Children.Add(new TextBlock { Text = "Record the actual permission or authorization process. A saved profile does not assert medical review.", Foreground = Brushes.DimGray, FontSize = 12 });
            var uploads = new WrapPanel(); form.Children.Add(uploads);
            uploads.Children.Add(Action("Upload signature PNG", () => { var image = ChooseImage(); if (image != null) signature = image; ShowAssets(); return Task.CompletedTask; }));
            uploads.Children.Add(Action("Upload stamp PNG", () => { var image = ChooseImage(); if (image != null) stamp = image; ShowAssets(); return Task.CompletedTask; }));
            var previews = new WrapPanel(); previews.Children.Add(signaturePreview); previews.Children.Add(stampPreview); form.Children.Add(previews); form.Children.Add(assetStatus);
            var clear = new WrapPanel(); form.Children.Add(clear);
            clear.Children.Add(Action("Remove signature", () => { signature = null; ShowAssets(); return Task.CompletedTask; }));
            clear.Children.Add(Action("Remove stamp", () => { stamp = null; ShowAssets(); return Task.CompletedTask; }));
            var buttons = new WrapPanel(); form.Children.Add(buttons);
            var save = Action("Save doctor", Save); save.Name = "SaveDoctorButton"; save.Style = (Style)FindResource("PrimaryButton"); buttons.Children.Add(save);
            buttons.Children.Add(Action("Set active / inactive", async () =>
            {
                if (selected == null) throw new ArgumentException("Choose a saved doctor first.");
                await api.Post<object?>("doctors/" + selected.DoctorId + "/active/" + (!selected.Active).ToString().ToLowerInvariant(), new { });
                await Refresh(); LibraryChanged?.Invoke(this, EventArgs.Empty); notify("Doctor availability updated. Saved report versions remain unchanged.");
            }));
            list.SelectionChanged += async (_, __) =>
            {
                if (loading || list.SelectedItem is not DoctorVersion doctor) return;
                await Run(async () => { selected = await api.Get<DoctorVersion>("doctors/versions/" + doctor.Id); LoadSelected(); });
            };
            NewDoctor();
        }
        public async Task Refresh()
        {
            var doctors = await api.Get<List<DoctorVersion>>("doctors");
            loading = true;
            try { list.ItemsSource = doctors.OrderBy(d => d.DisplayName).ToList(); list.SelectedItem = doctors.FirstOrDefault(d => d.DoctorId == selected?.DoctorId); }
            finally { loading = false; }
        }
        private void NewDoctor()
        {
            loading = true; selected = null; list.SelectedItem = null; foreach (var text in fields.Values) text.Clear();
            signature = null; stamp = null; heading.Text = "Add doctor"; ShowAssets(); loading = false;
        }
        private void LoadSelected()
        {
            var doctor = selected!;
            fields["name"].Text = doctor.Name; fields["display"].Text = doctor.DisplayName; fields["qualification"].Text = doctor.Qualification;
            fields["designation"].Text = doctor.Designation; fields["registration"].Text = doctor.RegistrationNumber; fields["specialty"].Text = doctor.Specialty;
            fields["evidence"].Text = doctor.PermissionEvidence; signature = doctor.SignaturePng; stamp = doctor.StampPng;
            heading.Text = doctor.DisplayName + " · version " + doctor.Version + (doctor.Active ? " · active" : " · inactive"); ShowAssets();
        }
        private async Task Save()
        {
            var name = fields["name"].Text.Trim(); var display = fields["display"].Text.Trim();
            var saved = await api.Post<DoctorVersion>("doctors/versions", new DoctorVersion { DoctorId = selected?.DoctorId ?? Guid.Empty,
                Name = name, DisplayName = display.Length == 0 ? name : display, Qualification = fields["qualification"].Text.Trim(),
                Designation = fields["designation"].Text.Trim(), RegistrationNumber = fields["registration"].Text.Trim(), Specialty = fields["specialty"].Text.Trim(),
                PermissionEvidence = fields["evidence"].Text.Trim(), SignaturePng = signature, StampPng = stamp, Active = selected?.Active ?? true });
            selected = await api.Get<DoctorVersion>("doctors/versions/" + saved.Id); LoadSelected(); await Refresh();
            LibraryChanged?.Invoke(this, EventArgs.Empty); notify("Doctor saved. Writers can select " + saved.DisplayName + " after refreshing their doctor library.");
        }
        private void Field(StackPanel form, string code, string label, int max)
        {
            form.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold });
            var text = new TextBox { Name = "Doctor_" + code, MaxLength = max }; fields.Add(code, text); form.Children.Add(text);
        }
        private Button Action(string caption, Func<Task> action)
        {
            var button = new Button { Content = caption, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += async (_, __) => await Run(action); return button;
        }
        private async Task Run(Func<Task> action)
        {
            IsEnabled = false;
            try { await action(); }
            catch (Exception error) { notify(error is ApiException || error is ArgumentException || error is FormatException ? error.Message : "Doctor library could not be updated. Check the host and try again; saved versions are retained."); }
            finally { IsEnabled = true; }
        }
        private static byte[]? ChooseImage()
        {
            var dialog = new OpenFileDialog { Filter = "PNG image|*.png", Multiselect = false };
            if (dialog.ShowDialog() != true) return null;
            if (new FileInfo(dialog.FileName).Length > 2 * 1024 * 1024) throw new ArgumentException("Choose a PNG smaller than 2 MiB.");
            var bytes = File.ReadAllBytes(dialog.FileName); Bitmap(bytes); return bytes;
        }
        private static BitmapImage? Bitmap(byte[]? bytes)
        {
            if (bytes == null) return null;
            using (var stream = new MemoryStream(bytes))
            {
                var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.StreamSource = stream; bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth = 260; bitmap.EndInit(); bitmap.Freeze(); return bitmap;
            }
        }
        private void ShowAssets()
        {
            signaturePreview.Source = Bitmap(signature); stampPreview.Source = Bitmap(stamp);
            assetStatus.Text = "Signature: " + (signature == null ? "none" : "selected") + "   ·   Stamp: " + (stamp == null ? "none" : "selected") + ". Existing images are kept unless replaced or removed.";
        }
        public void ClearProtectedState() { selected = null; signature = null; stamp = null; list.ItemsSource = null; signaturePreview.Source = null; stampPreview.Source = null; foreach (var field in fields.Values) field.Clear(); }
    }
}
