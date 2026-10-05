using System;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AkReporting.Contracts;

namespace AkReporting.Desktop
{
    public static class PagePresenter
    {
        private const double Scale = 96.0 / 72.0;
        public static FixedDocument Document(PagePlan plan)
        {
            var reportFont = new FontFamily(new Uri("pack://application:,,,/"), "./Assets/Fonts/#" + plan.FontFamily);
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(plan.Width * Scale, plan.Height * Scale);
            foreach (var page in plan.Pages)
            {
                var fixedPage = new FixedPage { Width = plan.Width * Scale, Height = plan.Height * Scale, Background = Brushes.White };
                foreach (var text in page.Text)
                {
                    var block = new TextBlock { Text = text.Text, Width = text.Width * Scale, Height = text.Height * Scale,
                        FontFamily = reportFont, FontSize = text.FontSize * Scale, FontWeight = text.Bold ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = text.Italic ? FontStyles.Italic : FontStyles.Normal, TextWrapping = TextWrapping.NoWrap,
                        TextAlignment = text.Alignment == Contracts.TextAlignment.Center ? System.Windows.TextAlignment.Center :
                            text.Alignment == Contracts.TextAlignment.Right ? System.Windows.TextAlignment.Right : System.Windows.TextAlignment.Left };
                    if (text.Underline) block.TextDecorations = TextDecorations.Underline;
                    FixedPage.SetLeft(block, text.X * Scale); FixedPage.SetTop(block, text.Y * Scale); fixedPage.Children.Add(block);
                }
                foreach (var image in page.Images)
                {
                    var bitmap = new BitmapImage();
                    using (var stream = new MemoryStream(image.Png, false))
                    { bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze(); }
                    var control = new Image { Source = bitmap, Width = image.Width * Scale, Height = image.Height * Scale, Stretch = Stretch.Fill };
                    FixedPage.SetLeft(control, image.X * Scale); FixedPage.SetTop(control, image.Y * Scale); fixedPage.Children.Add(control);
                }
                var content = new PageContent(); ((IAddChild)content).AddChild(fixedPage); document.Pages.Add(content);
            }
            return document;
        }
        public static void Print(PagePlan plan)
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            var requested = dialog.PrintTicket;
            requested.PageMediaSize = new PageMediaSize(PageMediaSizeName.ISOA4);
            requested.PageOrientation = PageOrientation.Portrait;
            requested.PageScalingFactor = 100;
            var validated = dialog.PrintQueue.MergeAndValidatePrintTicket(dialog.PrintTicket, requested).ValidatedPrintTicket;
            var capabilities = dialog.PrintQueue.GetPrintCapabilities(validated);
            var area = capabilities.PageImageableArea ?? throw new InvalidOperationException("Printer did not supply an imageable area.");
            if (validated.PageMediaSize?.PageMediaSizeName != PageMediaSizeName.ISOA4 || validated.PageOrientation != PageOrientation.Portrait ||
                plan.Left * Scale < area.OriginWidth || plan.Top * Scale < area.OriginHeight ||
                (plan.Width - plan.Right) * Scale > area.OriginWidth + area.ExtentWidth + 1 ||
                (plan.Height - plan.Bottom) * Scale > area.OriginHeight + area.ExtentHeight + 1)
                throw new InvalidOperationException("Printer cannot print this A4 template at 100% inside its imageable area. Calibrate the template/driver first.");
            dialog.PrintTicket = validated;
            dialog.PrintDocument(Document(plan).DocumentPaginator, "A K Diagnostic Report");
        }
    }
}
