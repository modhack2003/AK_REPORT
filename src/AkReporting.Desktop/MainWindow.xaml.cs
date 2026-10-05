using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
        private readonly WorkspaceNavigation navigation = new WorkspaceNavigation();
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
        private string caseQuery = "";
        public MainWindow()
        {
            InitializeComponent();
            Width = Math.Max(MinWidth, Math.Min(Width, SystemParameters.WorkArea.Width));
            Height = Math.Max(MinHeight, Math.Min(Height, SystemParameters.WorkArea.Height));
            Endpoint.Text = Environment.GetEnvironmentVariable("AK_API_URL") ?? "https://localhost:7043";
            AlignmentBox.ItemsSource = Enum.GetValues(typeof(Contracts.TextAlignment)); AlignmentBox.SelectedIndex = 0;
            FontFamilyBox.ItemsSource = new[] { "Noto Sans", "Noto Serif" }; FontFamilyBox.SelectedIndex = 0;
            Metadata.Load(new ReportMetadata());
            Metadata.Edited += (_, __) => MarkDirty(); ResultsEditor.Edited += (_, __) => MarkDirty();
            Catalog.LibraryChanged += async (_, __) => { if (api?.LoggedIn == true) await Run(RefreshDoctorLibrary); };
            Loaded += async (_, __) => await ShowStartup();
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
            Closed += (_, __) => { navigation.Invalidate(); timer.Stop(); InputManager.Current.PreProcessInput -= InputActivity; api?.Dispose(); };
            Closing += (_, e) => { if (!Discard()) e.Cancel = true; };
            loading = false;
            UpdateActions();
        }
        private async Task ShowStartup()
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AK Diagnostic Reporting", "branding", "logo.png");
            try
            {
                if (File.Exists(path))
                {
                    var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.UriSource = new Uri(path); image.EndInit(); image.Freeze();
                    foreach (var logo in new[] { SplashLogo, LoginLogo, HeaderLogo }) { logo.Source = image; logo.Visibility = Visibility.Visible; }
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is NotSupportedException || error is ArgumentException) { }
            await Task.Delay(1000);
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, __) => { SplashPanel.Visibility = Visibility.Collapsed; Username.Focus(); };
            SplashPanel.BeginAnimation(OpacityProperty, fade);
            Status.Text = "Sign in to start. Your administrator and writer use the same login page.";
        }
        private void UpdateActions()
        {
            if (SaveButton == null) return;
            ResultForm.IsEnabled = current != null;
            SaveButton.IsEnabled = current != null; SavePreviewButton.IsEnabled = current != null;
            AddTestsButton.Visibility = selectedCase != null ? Visibility.Visible : Visibility.Collapsed;
            CreateButton.Visibility = selectedCase != null && current != null ? Visibility.Collapsed : Visibility.Visible;
            PreviewHint.Visibility = plan == null ? Visibility.Visible : Visibility.Collapsed;
        }
        private void InputActivity(object sender, PreProcessInputEventArgs args) => lastInput = DateTime.UtcNow;
        private void MarkDirty() { if (!loading) { dirty = true; plan = null; Preview.Document = null; SaveState.Text = "Unsaved changes — choose Save draft or Save & preview."; UpdateActions(); } }
        private bool Discard() => !(dirty || Reason.Text.Length > 0 || AuthorizationBasis.Text.Length > 0) ||
            MessageBox.Show("Discard the unsaved entries in the current editor?", "Unsaved entries", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        private async Task Run(Func<Task> action)
        {
            if (busy) return;
            busy = true; Root.IsEnabled = false; BusyIndicator.Visibility = Visibility.Visible;
            try { await action(); }
            catch (ApiException e)
            {
                if ((int)e.Status == 401) LockSession(e.Message);
                else Status.Text = e.Message + ((int)e.Status == 409 && current != null
                    ? " Your entries remain in the editor. Use Load latest saved revision to review the saved record before re-entering your correction." : "");
            }
            catch (HttpRequestException) { HostUnavailable(); }
            catch (TaskCanceledException) { HostUnavailable(); }
            catch (Exception e) { Status.Text = e is ArgumentException || e is InvalidOperationException || e is FormatException ? e.Message : "Operation failed. Check the application host, network, printer or selected protected folder."; }
            finally { busy = false; Root.IsEnabled = true; BusyIndicator.Visibility = Visibility.Collapsed; UpdateActions(); if (LoginPanel.Visibility == Visibility.Visible) LoginFeedback.Text = Status.Text; }
        }
        private void HostUnavailable() => Status.Text = "The application host is unavailable or the request timed out. Current editor entries remain on screen. After a save attempt, load the latest saved revision to check whether it completed.";
        private async void Login_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (!Discard()) return;
            if (api != null)
            {
                try { await api.Logout(); }
                finally { LockSession("Signed out."); api.Dispose(); }
            }
            api = new ApiClient(Endpoint.Text.Trim());
            if (string.IsNullOrWhiteSpace(Username.Text) || Password.Password.Length == 0) throw new ArgumentException("Enter your username and password to sign in.");
            try { await api.Login(Username.Text.Trim(), Password.Password); } finally { Password.Clear(); }
            caseOffset = 0; caseQuery = "";
            var cases = await api.Get<List<CaseSummary>>("cases");
            if (api.Role != "Receptionist")
            {
                templates = await api.Get<List<TemplateDefinition>>("templates");
                doctors = await api.Get<List<DoctorVersion>>("doctors");
                Investigations.ItemsSource = templates.GroupBy(t => t.ReportTypeCode).Select(g => g.OrderByDescending(t => t.Version).First()).OrderBy(t => t.Title).ToList();
            }
            Cases.ItemsSource = cases;
            await Catalog.Connect(api, text => Status.Text = text);
            AdministrationTab.Visibility = api.Role == "Administrator" || api.Role == "MedicalReviewer" ? Visibility.Visible : Visibility.Collapsed;
            ManageButton.Visibility = AdministrationTab.Visibility;
            Tabs.SelectedIndex = 0; Workspace.IsEnabled = true; Workspace.Visibility = Visibility.Visible; LoginPanel.Visibility = Visibility.Collapsed;
            lastInput = DateTime.UtcNow; dirty = false; SignedInAs.Text = Username.Text.Trim() + " · " + api.Role;
            Status.Text = "Welcome. Choose New report, enter a name and date, tick the test, then Continue to results.";
        });
        private async void Logout_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (!Discard()) return;
            try { if (api != null) await api.Logout(); }
            finally { LockSession("Signed out."); }
        });
        private void LockSession(string message)
        {
            navigation.Invalidate();
            loading = true; api?.ClearSession(); Workspace.IsEnabled = false; current = null; selectedCase = null; selectedReport = null; template = null;
            Cases.ItemsSource = null; Reports.ItemsSource = null; Investigations.ItemsSource = null; Doctors.ItemsSource = null; History.ItemsSource = null;
            ResultsEditor.ClearProtectedState(); Metadata.Load(new ReportMetadata()); Catalog.ClearProtectedState(); Preview.Document = null; plan = null;
            templates.Clear(); doctors.Clear(); CaseQuery.Clear(); Reason.Clear(); AuthorizationBasis.Clear(); ReportTitle.Text = "Select a saved report";
            pendingReports.Clear(); caseOperation = Guid.NewGuid(); caseOffset = 0; caseQuery = ""; dirty = false; loading = false; Status.Text = message;
            Workspace.Visibility = Visibility.Collapsed; LoginPanel.Visibility = Visibility.Visible; LoginFeedback.Text = message; Password.Clear(); SignedInAs.Text = "";
            UpdateActions();
        }
        private void ClearReportEditor()
        {
            current = null; selectedReport = null; template = null;
            History.ItemsSource = null; Doctors.ItemsSource = null;
            ResultsEditor.ClearProtectedState(); Metadata.Load(new ReportMetadata()); Preview.Document = null; plan = null;
            Reason.Clear(); AuthorizationBasis.Clear(); ReportTitle.Text = "Select a saved report"; dirty = false;
            SaveState.Text = "Choose a patient and test in step 1."; UpdateActions();
        }
        private void ClearReportSelection()
        {
            loading = true;
            try { ClearReportEditor(); selectedCase = null; Cases.SelectedItem = null; Reports.ItemsSource = null; }
            finally { loading = false; }
        }
        private void RestoreSelections()
        {
            loading = true;
            try
            {
                Cases.SelectedItem = selectedCase; Reports.SelectedItem = selectedReport;
                History.SelectedItem = (History.ItemsSource as IEnumerable<ReportRevision>)?.SingleOrDefault(r => r.Id == current?.Id);
            }
            finally { loading = false; }
        }
        private async Task LoadCasePage(CaseSearchRequest request)
        {
            await navigation.TryReplace(async () =>
            {
                var cases = await api!.Post<List<CaseSummary>>("cases/search", request);
                if (cases == null) throw new InvalidOperationException("The application host returned no case list. Current entries have been retained.");
                return cases;
            }, Discard, cases =>
            {
                ClearReportSelection(); caseOffset = request.Offset; caseQuery = request.Query;
                CaseQuery.Text = caseQuery; Cases.ItemsSource = cases;
                Status.Text = "Saved case page loaded. Select a case to open its separate reports.";
            }, RestoreSelections);
        }
        private async void Refresh_Click(object sender, RoutedEventArgs e) => await Run(() => LoadCasePage(new CaseSearchRequest()));
        private async void Search_Click(object sender, RoutedEventArgs e) => await Run(() => LoadCasePage(new CaseSearchRequest { Query = CaseQuery.Text }));
        private async void Older_Click(object sender, RoutedEventArgs e) => await Run(() => LoadCasePage(new CaseSearchRequest { Query = caseQuery, Offset = caseOffset + 100 }));
        private void NewCase_Click(object sender, RoutedEventArgs e) { if (!Discard()) return; ClearReportSelection(); Investigations.UnselectAll(); caseOperation = Guid.NewGuid(); Tabs.SelectedIndex = 0; Status.Text = "New report: enter the patient name, choose the date and tick at least one test."; UpdateActions(); }
        private async void Create_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (current != null) throw new ArgumentException("Choose New case before creating another case. Use Add investigations for the selected case.");
            var metadata = Metadata.Read();
            if (api!.Role != "Receptionist" && Investigations.SelectedItems.Count == 0) throw new ArgumentException("Choose at least one test in step 1 to continue.");
            var c = await api!.Post<CaseSummary>("cases", new CreateCaseRequest { OperationId = caseOperation, Patient = metadata.Patient });
            selectedCase = c;
            if (api.Role != "Receptionist") await AddInvestigations(c, metadata);
            var cases = await api.Get<List<CaseSummary>>("cases");
            var reports = api.Role == "Receptionist" ? new List<ReportSummary>() : await api.Get<List<ReportSummary>>("cases/" + c.Id + "/reports");
            var first = reports.FirstOrDefault();
            var revision = first == null ? null : await FetchLatestRevision(first.Id);
            loading = true;
            try { Cases.ItemsSource = cases; Cases.SelectedItem = cases.FirstOrDefault(x => x.Id == c.Id); Reports.ItemsSource = reports; Reports.SelectedItem = first; }
            finally { loading = false; }
            if (revision != null) { LoadRevision(revision); selectedReport = first; Tabs.SelectedIndex = 1; }
            caseOperation = Guid.NewGuid(); dirty = false;
            pendingReports.Clear();
            Status.Text = "Report created. Enter available results; blank optional details stay blank. Choose Save & preview when ready.";
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
            // The caller releases IDs only after the refreshed report list is received.
        }
        private async void AddReports_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (selectedCase == null) throw new ArgumentException("Select a case first.");
            if (Investigations.SelectedItems.Count == 0) throw new ArgumentException("Tick at least one test in Patient & test before adding reports.");
            if (dirty && current != null) throw new ArgumentException("Save the current report before adding investigations.");
            await AddInvestigations(selectedCase, Metadata.Read());
            Reports.ItemsSource = await api!.Get<List<ReportSummary>>("cases/" + selectedCase.Id + "/reports"); Status.Text = "Selected investigations added as separate reports.";
            foreach (TemplateDefinition t in Investigations.SelectedItems) pendingReports.Remove(selectedCase.Id + "/" + t.VersionId);
        });
        private async void Cases_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loading || busy || Cases.SelectedItem is not CaseSummary c) return;
            await Run(async () =>
            {
                await navigation.TryReplace(async () =>
                {
                    var reports = api!.Role == "Receptionist" ? new List<ReportSummary>() : await api.Get<List<ReportSummary>>("cases/" + c.Id + "/reports");
                    if (reports == null || reports.Any(r => r.CaseId != c.Id)) throw new InvalidOperationException("The application host returned an invalid report list. Current entries have been retained.");
                    return reports;
                }, Discard, reports =>
                {
                    loading = true;
                    try
                    {
                        ClearReportEditor(); selectedCase = c; Cases.SelectedItem = c;
                        Metadata.Load(new ReportMetadata { Patient = c.Patient }); Reports.ItemsSource = reports;
                        Status.Text = "Saved case loaded. Select a report or add investigations.";
                    }
                    finally { loading = false; }
                }, RestoreSelections);
            });
        }
        private async void Reports_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loading || busy || Reports.SelectedItem is not ReportSummary r) return;
            await Run(async () =>
            {
                await navigation.TryReplace(() => FetchLatestRevision(r.Id), Discard, revision =>
                {
                    LoadRevision(revision); selectedReport = r; r.CurrentRevision = revision.Number;
                    Tabs.SelectedIndex = 1; Status.Text = "Latest saved report revision loaded.";
                }, RestoreSelections);
            });
        }
        private TemplateDefinition TemplateFor(ReportRevision revision) => templates.SingleOrDefault(t => t.VersionId == revision.Data.TemplateVersionId) ??
            throw new InvalidOperationException("The pinned template version is unavailable. Current entries have been retained. Reconnect to refresh the catalog.");
        private ReportRevision ValidateRevision(ReportRevision revision, Guid reportId)
        {
            if (revision == null || revision.ReportId != reportId || revision.CaseId != selectedCase?.Id || revision.Data == null ||
                revision.Data.Metadata == null || revision.Data.Metadata.Patient == null || revision.Data.Formatting == null || revision.Data.Results == null)
                throw new InvalidOperationException("The application host returned an invalid revision. Current entries have been retained.");
            TemplateFor(revision);
            return revision;
        }
        private async Task<ReportRevision> FetchLatestRevision(Guid reportId) => ValidateRevision(await api!.Get<ReportRevision>("reports/" + reportId), reportId);
        private void LoadRevision(ReportRevision revision, bool preserveHistory = false)
        {
            var selectedTemplate = TemplateFor(revision);
            var options = new List<DoctorVersion> { new DoctorVersion { Id = Guid.Empty, DisplayName = "[No doctor selected]" } };
            options.AddRange(doctors.Where(d => d.Active));
            if (revision.Data.DoctorVersionId is Guid pinned && options.All(d => d.Id != pinned))
                options.Add(new DoctorVersion { Id = pinned, DisplayName = "Pinned historical doctor version: " + pinned });
            loading = true;
            try
            {
                Metadata.Load(revision.Data.Metadata); ResultsEditor.Load(selectedTemplate, revision.Data);
                Doctors.ItemsSource = options; Doctors.SelectedItem = options.Single(d => d.Id == (revision.Data.DoctorVersionId ?? Guid.Empty));
                FontSizeBox.Text = revision.Data.Formatting.FontSize.ToString(CultureInfo.InvariantCulture);
                FontFamilyBox.SelectedItem = revision.Data.Formatting.FontFamily;
                BoldBox.IsChecked = revision.Data.Formatting.Bold; ItalicBox.IsChecked = revision.Data.Formatting.Italic; UnderlineBox.IsChecked = revision.Data.Formatting.Underline;
                AlignmentBox.SelectedItem = revision.Data.Formatting.Alignment;
                ReportTitle.Text = revision.PublicNumber + " • revision " + revision.Number + " • " + revision.State + " • " + selectedTemplate.Title + " v" + selectedTemplate.Version;
                current = revision; template = selectedTemplate;
                if (!preserveHistory) History.ItemsSource = null;
                else History.SelectedItem = revision;
                Reason.Clear(); AuthorizationBasis.Clear(); Preview.Document = null; plan = null; dirty = false;
                SaveState.Text = "Saved revision " + revision.Number + " · " + revision.State + ". Blank fields are allowed in a draft.";
                IssuePanel.IsEnabled = selectedTemplate.ReviewStatus == ReviewStatus.Approved;
                UpdateActions();
            }
            finally { loading = false; }
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
        private async void Save_Click(object sender, RoutedEventArgs e) => await Run(SaveDraft);
        private async Task SaveDraft()
        {
            RequireCurrent(false);
            var report = selectedReport!;
            var request = new SaveRevisionRequest { ExpectedRevision = current!.Number, Reason = Reason.Text, Draft = Draft() };
            await navigation.TryReplace(() => api!.Post<ReportRevision>("reports/" + report.Id + "/revisions", request), () => true, saved =>
            {
                LoadRevision(ValidateRevision(saved, report.Id)); report.CurrentRevision = saved.Number;
                Status.Text = "Revision " + saved.Number + " saved. Previous revisions preserved.";
            }, RestoreSelections);
        }
        private async void SavePreview_Click(object sender, RoutedEventArgs e) => await Run(async () => { if (dirty) await SaveDraft(); RequireCurrent(true); await PreviewSaved(); Tabs.SelectedIndex = 2; });
        private async void RefreshDoctors_Click(object sender, RoutedEventArgs e) => await Run(RefreshDoctorLibrary);
        private async Task RefreshDoctorLibrary()
        {
            var refreshed = await api!.Get<List<DoctorVersion>>("doctors");
            doctors = refreshed;
            if (current != null)
            {
                var pinned = (Doctors.SelectedItem as DoctorVersion)?.Id ?? Guid.Empty;
                var options = new List<DoctorVersion> { new DoctorVersion { Id = Guid.Empty, DisplayName = "No doctor selected (optional)" } };
                options.AddRange(doctors.Where(d => d.Active));
                if (pinned != Guid.Empty && options.All(d => d.Id != pinned) && Doctors.SelectedItem is DoctorVersion previous) options.Add(previous);
                loading = true;
                try { Doctors.ItemsSource = options; Doctors.SelectedItem = options.Single(d => d.Id == pinned); }
                finally { loading = false; }
            }
            Status.Text = "Doctor library refreshed. Select the doctor whose signature/stamp belongs on this report.";
        }
        private void Manage_Click(object sender, RoutedEventArgs e) => Tabs.SelectedIndex = 3;
        private void Tabs_Changed(object sender, SelectionChangedEventArgs e) { if (e.Source == Tabs && Tabs.SelectedIndex == 0) UpdateActions(); }
        private void Search_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Search_Click(sender, e); } }
        private void Login_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Login_Click(sender, e); } }
        private void Help_Click(object sender, RoutedEventArgs e) => MessageBox.Show("NEW REPORT\n1. Choose New report. Enter patient name and report date.\n2. Tick the test(s), then Continue to results.\n3. Enter available results. Doctor, age, notes and sample details are optional for drafts.\n4. Choose Save & preview, then Export PDF, Export Word or Print A4.\n\nSAVED REPORT\nSearch/select a patient on the left, then select their report.\n\nADMINISTRATOR\nUse Doctors & settings to add a doctor or update signature/stamp. Both administrators and writers can prepare reports.\n\nDraft configurations remain visibly marked until qualified medical review.", "Quick start", MessageBoxButton.OK, MessageBoxImage.Information);
        private async void Issue_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            RequireCurrent(false); if (dirty) throw new ArgumentException("Save the corrected draft before issuing.");
            var report = selectedReport!;
            var request = new IssueReportRequest { ExpectedRevision = current!.Number, Reason = Reason.Text, AuthorizationBasis = AuthorizationBasis.Text };
            await navigation.TryReplace(() => api!.Post<ReportRevision>("reports/" + report.Id + "/issue", request), () => true, issued =>
            {
                LoadRevision(ValidateRevision(issued, report.Id)); report.CurrentRevision = issued.Number;
                Status.Text = "Issued revision " + issued.Number + " saved with the recorded center authorization basis.";
            }, RestoreSelections);
        });
        private async void Reload_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (current == null || selectedReport == null) throw new ArgumentException("Select a saved report first.");
            var report = selectedReport;
            if (await navigation.TryReplace(() => FetchLatestRevision(report.Id), Discard, revision =>
            {
                LoadRevision(revision); report.CurrentRevision = revision.Number;
            }, RestoreSelections)) Status.Text = "Latest saved revision loaded. Review it before re-entering a correction; no entries were merged automatically.";
        });
        private async void History_Click(object sender, RoutedEventArgs e) => await Run(async () =>
        {
            if (current == null) throw new ArgumentException("Select a saved report first.");
            var reportId = current.ReportId; var revisionId = current.Id;
            await navigation.TryReplace(async () =>
            {
                var history = await api!.Get<List<ReportRevision>>("reports/" + reportId + "/history");
                if (history == null || history.Any(r => r.ReportId != reportId)) throw new InvalidOperationException("The application host returned an invalid revision history.");
                return history;
            }, () => true, history =>
            {
                loading = true;
                try { History.ItemsSource = history; History.SelectedItem = history.SingleOrDefault(r => r.Id == revisionId); }
                finally { loading = false; }
                Status.Text = "Revision history loaded. Selecting a revision replaces the editor after confirmation of unsaved entries.";
            }, RestoreSelections);
        });
        private async void History_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (loading || busy || History.SelectedItem is not ReportRevision r || selectedReport == null) return;
            await Run(async () =>
            {
                await navigation.TryReplace(() => Task.FromResult(ValidateRevision(r, selectedReport.Id)), Discard,
                    revision => LoadRevision(revision, preserveHistory: true), RestoreSelections);
            });
        }
        private void Editor_Changed(object sender, RoutedEventArgs e) => MarkDirty();
        private async Task PreviewSaved()
        {
            RequireCurrent(true); plan = await api!.Get<PagePlan>("reports/" + current!.ReportId + "/preview?revision=" + current.Number);
            Preview.Document = PagePresenter.Document(plan); Status.Text = "Saved revision preview: " + plan.Pages.Count + " A4 page(s).";
            UpdateActions();
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
