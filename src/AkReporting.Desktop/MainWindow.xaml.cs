using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using AkReporting.Contracts;
using Microsoft.Win32;

namespace AkReporting.Desktop
{
    public partial class MainWindow : Window
    {
        private ApiClient? api;
        private CaseSummary? selectedCase;
        private ReportSummary? selectedReport;
        private ReportRevision? current;
        private TemplateDefinition? template;
        private List<TemplateDefinition> templates = new List<TemplateDefinition>();
        private List<DoctorVersion> doctors = new List<DoctorVersion>();
        private readonly Dictionary<string, CreateReportRequest> pendingReports = new Dictionary<string, CreateReportRequest>();
        private Guid caseOperation = Guid.NewGuid();
        private PagePlan? plan;
        private bool loading = true;
        private bool busy;
        private bool dirty;
        private DateTime lastInput = DateTime.UtcNow;
        private DateTime lastHeartbeat = DateTime.MinValue;
        private bool heartbeatBusy;
        private int caseOffset;
        public MainWindow()
        {
            InitializeComponent();
            Endpoint.Text = Environment.GetEnvironmentVariable("AK_API_URL") ?? "https://localhost:7043";
            AlignmentBox.ItemsSource = Enum.GetValues(typeof(Contracts.TextAlignment)); AlignmentBox.SelectedIndex = 0;
            FontFamilyBox.ItemsSource = new[] { "Noto Sans", "Noto Serif" }; FontFamilyBox.SelectedIndex = 0;
            Metadata.Load(new ReportMetadata());
            Metadata.Edited += (_, __) => MarkDirty(); ResultsEditor.Edited += (_, __) => MarkDirty();
            InputManager.Current.PreProcessInput += InputActivity;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            timer.Tick += async (_, __) =>
            {
                if (api?.LoggedIn != true || busy || heartbeatBusy) return;
                var now = DateTime.UtcNow;
                if (now - lastInput > TimeSpan.FromMinutes(15)) { LockSession("Session timed out. Sign in again."); return; }
                if (now - lastInput < TimeSpan.FromMinutes(1) && now - lastHeartbeat > TimeSpan.FromMinutes(1))
                {
                    lastHeartbeat = now;
                    heartbeatBusy = true;
                    var heartbeatClient = api;
                    try { await heartbeatClient.Get<object>("auth/activity"); }
                    catch (ApiException error) { if (ReferenceEquals(api, heartbeatClient) && (int)error.Status == 401) LockSession(error.Message); }
                    catch (Exception) { if (ReferenceEquals(api, heartbeatClient)) Status.Text = "Local application host is unavailable. Unsaved entries remain on screen until session lock or application close."; }
                    finally { heartbeatBusy = false; }
                }
            };
            timer.Start();
            Closed += (_, __) => { timer.Stop(); InputManager.Current.PreProcessInput -= InputActivity; api?.Dispose(); };
            Closing += (_, e) => { if (dirty && !Discard()) e.Cancel = true; };
            loading = false;
        }
        private void InputActivity(object sender, PreProcessInputEventArgs args) => lastInput = DateTime.UtcNow;
        private void MarkDirty() { if (!loading) { dirty = true; plan = null; Preview.Document = null; } }
        private bool Discard() => !dirty || MessageBox.Show("Discard unsaved entries and load the selected saved record?", "Unsaved entries", MessageBoxButton.YesNo) == MessageBoxResult.Yes;
        private async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true; Root.IsEnabled = false;
            try { await action(); }
            catch (ApiException e) { if ((int)e.Status == 401) LockSession(e.Message); else Status.Text = e.Message; }
            catch (Exception e) { Status.Text = e is ArgumentException || e is InvalidOperationException || e is FormatException ? e.Message : "Operation failed. Check the application host, network, printer or selected protected folder."; }
            finally { busy = false; Root.IsEnabled = true; }
        }
        private async void Login_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (!Discard()) return;
            if (api != null)
            {
                try { await api.Logout(); }
                finally { LockSession("Signed out."); api.Dispose(); }
            }
            api = new ApiClient(Endpoint.Text.Trim());
            try { await api.Login(Username.Text.Trim(), Password.Password); } finally { Password.Clear(); }
            Workspace.IsEnabled = true; lastInput = DateTime.UtcNow; dirty = false;
            Catalog.Connect(api, text => Status.Text = text);
            if (api.Role == "Administrator") { Tabs.SelectedIndex = 3; }
            else
            {
                caseOffset = 0; Cases.ItemsSource = await api.Get<List<CaseSummary>>("cases");
                if (api.Role != "Receptionist")
                {
                    templates = await api.Get<List<TemplateDefinition>>("templates");
                    doctors = await api.Get<List<DoctorVersion>>("doctors");
                    Investigations.ItemsSource = templates.GroupBy(t => t.ReportTypeCode).Select(g => g.OrderByDescending(t => t.Version).First()).ToList();
                }
            }
            Status.Text = "Signed in as " + api.Role + ". Draft schemas require medical review before issue.";
        });
        private async void Logout_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (!Discard()) return;
            try { if (api != null) await api.Logout(); }
            finally { LockSession("Signed out."); }
        });
        private void LockSession(string message)
        {
            loading = true; api?.ClearSession(); Workspace.IsEnabled = false; current = null; selectedCase = null; selectedReport = null;
            Cases.ItemsSource = null; Reports.ItemsSource = null; Investigations.ItemsSource = null; Doctors.ItemsSource = null; History.ItemsSource = null;
            ResultsEditor.ClearProtectedState(); Metadata.Load(new ReportMetadata()); Catalog.ClearProtectedState(); Preview.Document = null; plan = null;
            templates.Clear(); doctors.Clear(); CaseQuery.Clear(); Reason.Clear(); AuthorizationBasis.Clear(); ReportTitle.Text = "Select a saved report";
            pendingReports.Clear(); caseOperation = Guid.NewGuid(); dirty = false; loading = false; Status.Text = message;
        }
        private void ClearReportSelection()
        {
            loading = true; current = null; selectedCase = null; selectedReport = null; template = null;
            Cases.SelectedItem = null; Reports.ItemsSource = null; History.ItemsSource = null; Doctors.ItemsSource = null;
            ResultsEditor.ClearProtectedState(); Metadata.Load(new ReportMetadata()); Preview.Document = null; plan = null;
            Reason.Clear(); AuthorizationBasis.Clear(); ReportTitle.Text = "Select a saved report";
            dirty = false; loading = false;
        }
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(async () => { if (!Discard()) return; ClearReportSelection(); caseOffset = 0; Cases.ItemsSource = await api!.Get<List<CaseSummary>>("cases"); });
        private async void Search_Click(object sender, RoutedEventArgs e) => await Run(async () => { if (!Discard()) return; ClearReportSelection(); caseOffset = 0; Cases.ItemsSource = await api!.Post<List<CaseSummary>>("cases/search", new CaseSearchRequest { Query = CaseQuery.Text }); });
        private async void Older_Click(object sender, RoutedEventArgs e) => await Run(async () => { if (!Discard()) return; ClearReportSelection(); caseOffset += 100; Cases.ItemsSource = await api!.Post<List<CaseSummary>>("cases/search", new CaseSearchRequest { Query = CaseQuery.Text, Offset = caseOffset }); });
        private void NewCase_Click(object sender, RoutedEventArgs e) { if (!Discard()) return; ClearReportSelection(); caseOperation = Guid.NewGuid(); Tabs.SelectedIndex = 0; }
        private async void Create_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (current != null) throw new ArgumentException("Choose New case before creating another case. Use Add investigations for the selected case.");
            var metadata = Metadata.Read();
            var c = await api!.Post<CaseSummary>("cases", new CreateCaseRequest { OperationId = caseOperation, Patient = metadata.Patient });
            selectedCase = c;
            if (api.Role != "Receptionist") await AddInvestigations(c, metadata);
            caseOperation = Guid.NewGuid(); dirty = false;
            loading = true; Cases.ItemsSource = await api.Get<List<CaseSummary>>("cases"); Cases.SelectedItem = ((List<CaseSummary>)Cases.ItemsSource).Single(x => x.Id == c.Id); loading = false;
            if (api.Role != "Receptionist") Reports.ItemsSource = await api.Get<List<ReportSummary>>("cases/" + c.Id + "/reports");
            Status.Text = "Case saved; each investigation is an independent draft report.";
        });
        private async Task AddInvestigations(CaseSummary c, ReportMetadata metadata)
        {
            foreach (TemplateDefinition t in Investigations.SelectedItems)
            {
                var key = c.Id + "/" + t.VersionId;
                if (!pendingReports.TryGetValue(key, out var request))
                {
                    request = new CreateReportRequest { OperationId = Guid.NewGuid(), CaseId = c.Id, Draft = new ReportDraft { TemplateVersionId = t.VersionId,
                        Formatting = new ReportFormatting { FontFamily = t.Layout.FontFamily, FontSize = t.Layout.FontSize },
                        Metadata = new ReportMetadata { Patient = c.Patient, ReferringDoctor = metadata.ReferringDoctor, ClinicalHistory = metadata.ClinicalHistory,
                            CollectionTime = metadata.CollectionTime, ReportTime = metadata.ReportTime, Specimen = metadata.Specimen, SpecimenId = metadata.SpecimenId,
                            Method = metadata.Method, TechnicianAttribution = metadata.TechnicianAttribution } } };
                    pendingReports.Add(key, request);
                }
                await api!.Post<ReportRevision>("reports", request);
            }
            // Keep completed operation IDs until the whole batch succeeds, so a partial
            // network failure and retry cannot recreate investigations already committed.
            foreach (TemplateDefinition t in Investigations.SelectedItems) pendingReports.Remove(c.Id + "/" + t.VersionId);
        }
        private async void AddReports_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (selectedCase == null) throw new ArgumentException("Select a case first.");
            if (dirty && current != null) throw new ArgumentException("Save the current report before adding investigations.");
            await AddInvestigations(selectedCase, Metadata.Read());
            Reports.ItemsSource = await api!.Get<List<ReportSummary>>("cases/" + selectedCase.Id + "/reports"); Status.Text = "Selected investigations added as separate reports.";
        });
        private async void Cases_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loading || busy || Cases.SelectedItem is not CaseSummary c) return;
            if (!Discard()) { loading = true; Cases.SelectedItem = selectedCase; loading = false; return; }
            await Run(async () =>
            {
                selectedCase = c; current = null; selectedReport = null; dirty = false;
                Metadata.Load(new ReportMetadata { Patient = c.Patient }); ResultsEditor.Children.Clear(); Preview.Document = null; plan = null;
                if (api!.Role != "Receptionist") Reports.ItemsSource = await api.Get<List<ReportSummary>>("cases/" + c.Id + "/reports");
            });
        }
        private async void Reports_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loading || busy || Reports.SelectedItem is not ReportSummary r) return;
            if (!Discard()) { loading = true; Reports.SelectedItem = selectedReport; loading = false; return; }
            await Run(async () => { selectedReport = r; await LoadRevision(await api!.Get<ReportRevision>("reports/" + r.Id)); Tabs.SelectedIndex = 1; });
        }
        private Task LoadRevision(ReportRevision revision)
        {
            loading = true; current = revision; template = templates.Single(t => t.VersionId == revision.Data.TemplateVersionId);
            Metadata.Load(revision.Data.Metadata); ResultsEditor.Load(template, revision.Data);
            var options = new List<DoctorVersion> { new DoctorVersion { Id = Guid.Empty, DisplayName = "[No doctor selected]" } };
            options.AddRange(doctors.Where(d => d.Active));
            if (revision.Data.DoctorVersionId is Guid pinned && options.All(d => d.Id != pinned))
                options.Add(new DoctorVersion { Id = pinned, DisplayName = "Pinned historical doctor version: " + pinned });
            Doctors.ItemsSource = options; Doctors.SelectedItem = options.Single(d => d.Id == (revision.Data.DoctorVersionId ?? Guid.Empty));
            FontSizeBox.Text = revision.Data.Formatting.FontSize.ToString(CultureInfo.InvariantCulture);
            FontFamilyBox.SelectedItem = revision.Data.Formatting.FontFamily;
            BoldBox.IsChecked = revision.Data.Formatting.Bold; ItalicBox.IsChecked = revision.Data.Formatting.Italic; UnderlineBox.IsChecked = revision.Data.Formatting.Underline;
            AlignmentBox.SelectedItem = revision.Data.Formatting.Alignment;
            ReportTitle.Text = revision.PublicNumber + " • revision " + revision.Number + " • " + revision.State + " • " + template.Title + " v" + template.Version;
            Reason.Clear(); AuthorizationBasis.Clear(); Preview.Document = null; plan = null; dirty = false; loading = false;
            return Task.CompletedTask;
        }
        private ReportDraft Draft()
        {
            if (template == null) throw new ArgumentException("Select a report first.");
            var doctor = Doctors.SelectedItem as DoctorVersion;
            return new ReportDraft { TemplateVersionId = template.VersionId, DoctorVersionId = doctor == null || doctor.Id == Guid.Empty ? null : doctor.Id,
                Metadata = Metadata.Read(), Results = ResultsEditor.Read(), Formatting = new ReportFormatting
                { FontFamily = (string)FontFamilyBox.SelectedItem, FontSize = double.Parse(FontSizeBox.Text, CultureInfo.InvariantCulture), Bold = BoldBox.IsChecked == true, Italic = ItalicBox.IsChecked == true,
                    Underline = UnderlineBox.IsChecked == true, Alignment = (Contracts.TextAlignment)AlignmentBox.SelectedItem,
                    HiddenSections = ResultsEditor.HiddenSections() } };
        }
        private void RequireCurrent(bool savedOnly)
        {
            if (current == null) throw new ArgumentException("Select a saved report first.");
            if (!savedOnly && current.Number != selectedReport?.CurrentRevision) throw new ArgumentException("Load the latest revision before correcting or issuing.");
            if (savedOnly && dirty) throw new ArgumentException("Save a new revision before preview, generation or printing.");
        }
        private async void Save_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            RequireCurrent(false);
            var saved = await api!.Post<ReportRevision>("reports/" + current!.ReportId + "/revisions", new SaveRevisionRequest { ExpectedRevision = current.Number, Reason = Reason.Text, Draft = Draft() });
            selectedReport!.CurrentRevision = saved.Number; await LoadRevision(saved); Status.Text = "Revision " + saved.Number + " saved. Previous revisions preserved.";
        });
        private async void Issue_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            RequireCurrent(false); if (dirty) throw new ArgumentException("Save the corrected draft before issuing.");
            var issued = await api!.Post<ReportRevision>("reports/" + current!.ReportId + "/issue", new IssueReportRequest { ExpectedRevision = current.Number, Reason = Reason.Text, AuthorizationBasis = AuthorizationBasis.Text });
            selectedReport!.CurrentRevision = issued.Number; await LoadRevision(issued); Status.Text = "Issued revision " + issued.Number + " saved with the recorded center authorization basis.";
        });
        private async void History_Click(object sender, RoutedEventArgs e) => await Run(async () => { RequireCurrent(true); History.ItemsSource = await api!.Get<List<ReportRevision>>("reports/" + current!.ReportId + "/history"); });
        private async void History_Changed(object sender, SelectionChangedEventArgs e)
        { if (!loading && !busy && History.SelectedItem is ReportRevision r && Discard()) await Run(() => LoadRevision(r)); }
        private void Editor_Changed(object sender, RoutedEventArgs e) => MarkDirty();
        private async Task PreviewSaved()
        {
            RequireCurrent(true); plan = await api!.Get<PagePlan>("reports/" + current!.ReportId + "/preview?revision=" + current.Number);
            Preview.Document = PagePresenter.Document(plan); Status.Text = "Saved revision preview: " + plan.Pages.Count + " A4 page(s).";
        }
        private async void Preview_Click(object sender, RoutedEventArgs e) => await Run(PreviewSaved);
        private async void Print_Click(object sender, RoutedEventArgs e) => await Run(async () => { await PreviewSaved(); PagePresenter.Print(plan!); Status.Text = "Print dialog completed. Verify physical output; spool submission does not confirm printing."; });
        private async void Pdf_Click(object sender, RoutedEventArgs e) => await Run(() => Export("pdf"));
        private async void Docx_Click(object sender, RoutedEventArgs e) => await Run(() => Export("docx"));
        private async Task Export(string format)
        {
            RequireCurrent(true);
            var dialog = new SaveFileDialog { Filter = format.ToUpperInvariant() + " document|*." + format, DefaultExt = "." + format,
                FileName = current!.PublicNumber + "-v" + current.Number + "." + format, OverwritePrompt = true };
            if (dialog.ShowDialog() != true) return;
            var info = await api!.Post<GeneratedDocumentInfo>("reports/" + current.ReportId + "/documents/" + format + "?revision=" + current.Number, new { });
            SafeExport.Write(dialog.FileName, await api.Download(info.Id), info); Status.Text = "Verified " + format.ToUpperInvariant() + " document exported.";
        }
    }
}
