using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AkReporting.Contracts;

namespace AkReporting.Desktop
{
    public sealed class ResultEditor : StackPanel
    {
        private sealed class Cell
        {
            public SectionDefinition Section = null!;
            public FieldDefinition Field = null!;
            public int Row;
            public Control Input = null!;
            public ComboBox? Comparator;
        }
        private readonly List<Cell> cells = new List<Cell>();
        private readonly Dictionary<string, CheckBox> visibility = new Dictionary<string, CheckBox>();
        private bool loading;
        public event EventHandler? Edited;
        public void ClearProtectedState() { Children.Clear(); cells.Clear(); visibility.Clear(); }
        public void Load(TemplateDefinition template, ReportDraft draft)
        {
            loading = true; Children.Clear(); cells.Clear(); visibility.Clear();
            foreach (var section in template.Sections)
            {
                var content = new StackPanel { Margin = new Thickness(10) };
                Children.Add(new Expander { Header = section.Title, Content = content, IsExpanded = true, Margin = new Thickness(0, 4, 0, 10) });
                if (section.Optional)
                {
                    var hide = new CheckBox { Content = "Hide only when empty", IsChecked = draft.Formatting.HiddenSections.Contains(section.Code) };
                    hide.Checked += Changed; hide.Unchecked += Changed;
                    content.Children.Add(hide); visibility.Add(section.Code, hide);
                }
                var rows = section.Repeatable ? draft.Results.Where(r => r.SectionCode == section.Code).Select(r => r.Row).Distinct().OrderBy(r => r).DefaultIfEmpty(0) : new[] { 0 };
                foreach (var row in rows) AddRow(content, section, row, draft.Results);
                if (section.Repeatable)
                {
                    var add = new Button { Content = "Add structured row" };
                    add.Click += (_, __) =>
                    {
                        var next = cells.Where(c => c.Section == section).Max(c => c.Row) + 1;
                        if (next > 999) throw new ArgumentException("Maximum table rows reached.");
                        AddRow(content, section, next, new List<ReportResult>()); Changed(this, new RoutedEventArgs());
                    };
                    content.Children.Add(add);
                }
            }
            loading = false;
        }
        private void AddRow(StackPanel parent, SectionDefinition section, int row, List<ReportResult> values)
        {
            foreach (var field in section.Fields)
            {
                var value = values.SingleOrDefault(r => r.SectionCode == section.Code && r.FieldCode == field.Code && r.Row == row);
                parent.Children.Add(new TextBlock { Text = field.Label + (field.Required ? " *" : "") + (section.Repeatable ? " [row " + (row + 1) + "]" : ""), FontWeight = FontWeights.SemiBold });
                if (field.Unit.Length > 0 || field.ReferenceText.Length > 0)
                    parent.Children.Add(new TextBlock { Text = field.Unit + "  " + field.ReferenceText, Foreground = Brushes.DimGray });
                Control input;
                if (field.Choices.Count > 0)
                {
                    var choice = new ComboBox { ItemsSource = new[] { "" }.Concat(field.Choices), SelectedItem = value?.TextValue ?? "" };
                    choice.SelectionChanged += (_, __) => Changed(this, new RoutedEventArgs()); input = choice;
                }
                else
                {
                    var text = new TextBox { Text = value?.NumericValue?.ToString(CultureInfo.InvariantCulture) ?? value?.TextValue ?? "", MaxLength = field.MaxLength,
                        AcceptsReturn = field.Kind == ResultKind.Multiline, MinHeight = field.Kind == ResultKind.Multiline ? 90 : 0 };
                    text.TextChanged += (_, __) => Changed(this, new RoutedEventArgs()); input = text;
                }
                var cell = new Cell { Section = section, Field = field, Row = row, Input = input };
                if (field.Kind == ResultKind.Numeric && field.AllowComparator)
                {
                    cell.Comparator = new ComboBox { ItemsSource = new[] { "", "<", "<=", ">", ">=" }, SelectedItem = value?.Comparator ?? "" };
                    cell.Comparator.SelectionChanged += (_, __) => Changed(this, new RoutedEventArgs()); parent.Children.Add(cell.Comparator);
                }
                parent.Children.Add(input); cells.Add(cell);
            }
        }
        public List<ReportResult> Read()
        {
            var values = new List<ReportResult>();
            foreach (var cell in cells)
            {
                var text = cell.Input is TextBox box ? box.Text : (string?)((ComboBox)cell.Input).SelectedItem ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;
                var result = new ReportResult { SectionCode = cell.Section.Code, FieldCode = cell.Field.Code, Row = cell.Row,
                    Kind = cell.Field.Kind, Unit = cell.Field.Unit, ReferenceText = cell.Field.ReferenceText };
                if (cell.Field.Kind == ResultKind.Numeric)
                {
                    if (!decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numeric))
                        throw new ArgumentException(cell.Field.Label + ": enter a decimal number using a dot. No thousands separator or automatic calculation.");
                    result.NumericValue = numeric; result.Comparator = (string?)cell.Comparator?.SelectedItem ?? "";
                }
                else result.TextValue = text;
                values.Add(result);
            }
            return values;
        }
        public List<string> HiddenSections() => visibility.Where(p => p.Value.IsChecked == true).Select(p => p.Key).ToList();
        private void Changed(object? sender, RoutedEventArgs args) { if (!loading) Edited?.Invoke(this, EventArgs.Empty); }
    }
}
