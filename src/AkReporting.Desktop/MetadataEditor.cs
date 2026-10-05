using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using AkReporting.Contracts;

namespace AkReporting.Desktop
{
    public sealed class MetadataEditor : StackPanel
    {
        private readonly Dictionary<string, TextBox> inputs = new Dictionary<string, TextBox>();
        private readonly DatePicker reportDate = new DatePicker { Name = "ReportDate", SelectedDateFormat = DatePickerFormat.Long };
        private readonly DatePicker collectionDate = new DatePicker { SelectedDateFormat = DatePickerFormat.Long };
        private readonly ComboBox ageUnit = new ComboBox { ItemsSource = new[] { "years", "months", "days" }, SelectedIndex = 0 };
        private readonly ComboBox sex = new ComboBox { ItemsSource = new[] { "", "Female", "Male", "Other", "Not recorded" }, IsEditable = true };
        private DateTimeOffset originalReportTime;
        private DateTimeOffset? originalCollectionTime;
        private bool loading;
        public event EventHandler? Edited;

        public MetadataEditor()
        {
            Children.Add(new TextBlock { Text = "Patient details", FontSize = 22, FontWeight = FontWeights.SemiBold });
            Children.Add(new TextBlock { Text = "Name, date and a test are enough to start. Other details are optional.", Margin = new Thickness(0, 8, 0, 12) });
            var required = new Grid(); required.ColumnDefinitions.Add(new ColumnDefinition()); required.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
            var name = Field("name", "Patient name *", 200); name.Margin = new Thickness(0, 0, 24, 0); required.Children.Add(name);
            var date = Labelled("Report date *", reportDate); Grid.SetColumn(date, 1); required.Children.Add(date); Children.Add(required);
            var optional = new StackPanel();
            Children.Add(new Expander { Header = "Optional patient / specimen details", Content = optional, IsExpanded = false, Margin = new Thickness(0, 8, 0, 0) });
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition());
            var id = Field("id", "Patient ID (optional)", 80); id.Margin = new Thickness(0, 0, 16, 0); row.Children.Add(id);
            var agePanel = new StackPanel { Margin = new Thickness(0, 0, 16, 0) }; agePanel.Children.Add(Field("age", "Age (optional)", 5)); agePanel.Children.Add(ageUnit);
            Grid.SetColumn(agePanel, 1); row.Children.Add(agePanel); var sexPanel = Labelled("Sex (optional)", sex); Grid.SetColumn(sexPanel, 2); row.Children.Add(sexPanel); optional.Children.Add(row);
            optional.Children.Add(Field("referrer", "Referring doctor (optional)", 200));
            optional.Children.Add(Field("history", "Clinical history / notes (optional)", 10000, true));
            optional.Children.Add(Field("specimen", "Sample / specimen (optional)", 2000));
            optional.Children.Add(Field("specimenId", "Specimen ID (optional)", 100));
            optional.Children.Add(Field("method", "Method (optional)", 2000));
            optional.Children.Add(Field("technician", "Technician attribution (optional)", 500));
            optional.Children.Add(Labelled("Collection date (optional)", collectionDate));
            reportDate.SelectedDateChanged += Changed; collectionDate.SelectedDateChanged += Changed;
            ageUnit.SelectionChanged += Changed; sex.SelectionChanged += Changed;
            sex.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(Changed));
        }
        private StackPanel Field(string code, string label, int max, bool multiline = false)
        {
            var text = new TextBox { Name = code == "name" ? "PatientName" : "Patient_" + code, MaxLength = max, AcceptsReturn = multiline, MinHeight = multiline ? 80 : 38, VerticalContentAlignment = VerticalAlignment.Center };
            text.TextChanged += Changed; inputs.Add(code, text); return Labelled(label, text);
        }
        private static StackPanel Labelled(string label, Control input)
        {
            var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 4) });
            panel.Children.Add(input); return panel;
        }
        public void Load(ReportMetadata data)
        {
            loading = true;
            Set("name", data.Patient.Name); Set("id", data.Patient.LocalId); Set("age", data.Patient.Age?.ToString(CultureInfo.InvariantCulture) ?? "");
            ageUnit.SelectedItem = string.IsNullOrEmpty(data.Patient.AgeUnit) ? "years" : data.Patient.AgeUnit; sex.Text = data.Patient.Sex;
            Set("referrer", data.ReferringDoctor); Set("history", data.ClinicalHistory); Set("specimen", data.Specimen); Set("specimenId", data.SpecimenId);
            Set("method", data.Method); Set("technician", data.TechnicianAttribution);
            originalReportTime = data.ReportTime == default ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)) : data.ReportTime;
            originalCollectionTime = data.CollectionTime;
            reportDate.SelectedDate = originalReportTime.Date; collectionDate.SelectedDate = data.CollectionTime?.Date;
            loading = false;
        }
        public PatientMetadata Patient()
        {
            if (Get("name").Length == 0) { inputs["name"].Focus(); throw new ArgumentException("Enter the patient name to continue."); }
            int? age = null;
            if (Get("age").Length > 0)
            {
                if (!int.TryParse(Get("age"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                { inputs["age"].Focus(); throw new ArgumentException("Age is optional. Enter a whole number or leave it blank."); }
                age = parsed;
            }
            return new PatientMetadata { Name = Get("name"), LocalId = Get("id"), Age = age, AgeUnit = age.HasValue ? (string)ageUnit.SelectedItem : "", Sex = sex.Text.Trim() };
        }
        public ReportMetadata Read()
        {
            var patient = Patient();
            if (!reportDate.SelectedDate.HasValue) { reportDate.Focus(); throw new ArgumentException("Choose the report date to continue."); }
            return new ReportMetadata { Patient = patient, ReferringDoctor = Get("referrer"), ClinicalHistory = Get("history"), Specimen = Get("specimen"),
                SpecimenId = Get("specimenId"), Method = Get("method"), TechnicianAttribution = Get("technician"),
                ReportTime = DateValue(reportDate.SelectedDate.Value, originalReportTime),
                CollectionTime = collectionDate.SelectedDate.HasValue ? DateValue(collectionDate.SelectedDate.Value, originalCollectionTime) : (DateTimeOffset?)null };
        }
        private static DateTimeOffset DateValue(DateTime day, DateTimeOffset? original)
        {
            // Preserve existing precision and offset when the selected day is unchanged.
            if (original.HasValue && original.Value.Date == day.Date) return original.Value;
            var offset = original?.Offset ?? TimeSpan.FromMinutes(330);
            var time = original?.TimeOfDay ?? TimeSpan.Zero;
            return new DateTimeOffset(DateTime.SpecifyKind(day.Date + time, DateTimeKind.Unspecified), offset);
        }
        private void Changed(object? sender, RoutedEventArgs args) { if (!loading) Edited?.Invoke(this, EventArgs.Empty); }
        private string Get(string code) => inputs[code].Text.Trim();
        private void Set(string code, string value) => inputs[code].Text = value;
    }
}
