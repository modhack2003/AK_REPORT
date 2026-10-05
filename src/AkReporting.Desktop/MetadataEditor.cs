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
        public event EventHandler? Edited;
        private bool loading;
        public MetadataEditor()
        {
            foreach (var item in new[] { ("name", "Patient name"), ("id", "Patient ID"), ("age", "Age"), ("ageUnit", "Age unit: years / months / days"),
                ("sex", "Sex as recorded"), ("referrer", "Referring doctor"), ("history", "Clinical history"), ("specimen", "Sample / specimen"),
                ("specimenId", "Specimen ID"), ("method", "Method, where applicable"), ("technician", "Actual technician attribution"),
                ("collection", "Collection time (optional, ISO with offset)"), ("reportTime", "Report time (ISO with offset)") })
            {
                Children.Add(new TextBlock { Text = item.Item2 });
                var text = new TextBox { MaxLength = item.Item1 == "history" ? 10000 : 2000,
                    AcceptsReturn = item.Item1 == "history", MinHeight = item.Item1 == "history" ? 60 : 0 };
                text.TextChanged += (_, __) => { if (!loading) Edited?.Invoke(this, EventArgs.Empty); };
                inputs.Add(item.Item1, text); Children.Add(text);
            }
        }
        public void Load(ReportMetadata data)
        {
            loading = true;
            Set("name", data.Patient.Name); Set("id", data.Patient.LocalId); Set("age", data.Patient.Age?.ToString(CultureInfo.InvariantCulture) ?? "");
            Set("ageUnit", data.Patient.AgeUnit); Set("sex", data.Patient.Sex); Set("referrer", data.ReferringDoctor); Set("history", data.ClinicalHistory);
            Set("specimen", data.Specimen); Set("specimenId", data.SpecimenId); Set("method", data.Method); Set("technician", data.TechnicianAttribution);
            Set("collection", data.CollectionTime?.ToString("O", CultureInfo.InvariantCulture) ?? "");
            Set("reportTime", data.ReportTime == default ? DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).ToString("O") : data.ReportTime.ToString("O"));
            loading = false;
        }
        public PatientMetadata Patient()
        {
            int? age = null;
            if (Get("age").Length > 0)
            {
                if (!int.TryParse(Get("age"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                    throw new ArgumentException("Age must be an explicit nonnegative integer.");
                age = parsed;
            }
            return new PatientMetadata { Name = Get("name"), LocalId = Get("id"), Age = age, AgeUnit = Get("ageUnit"), Sex = Get("sex") };
        }
        public ReportMetadata Read() => new ReportMetadata
        {
            Patient = Patient(), ReferringDoctor = Get("referrer"), ClinicalHistory = Get("history"), Specimen = Get("specimen"),
            SpecimenId = Get("specimenId"), Method = Get("method"), TechnicianAttribution = Get("technician"),
            CollectionTime = Get("collection").Length == 0 ? null : Time(Get("collection")), ReportTime = Time(Get("reportTime"))
        };
        private static DateTimeOffset Time(string text)
        {
            if (!DateTimeOffset.TryParseExact(text, new[] { "O", "yyyy-MM-dd HH:mm:ss zzz", "yyyy-MM-dd HH:mm zzz" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
                throw new ArgumentException("Enter date/time with an explicit offset, e.g. 2026-01-02 10:15 +05:30.");
            return value;
        }
        private string Get(string code) => inputs[code].Text.Trim();
        private void Set(string code, string value) => inputs[code].Text = value;
    }
}
