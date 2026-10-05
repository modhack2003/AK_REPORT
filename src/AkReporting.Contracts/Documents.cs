using System;
using System.Collections.Generic;

namespace AkReporting.Contracts
{
    public sealed class PositionedText
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public string Text { get; set; } = "";
        public double FontSize { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public TextAlignment Alignment { get; set; }
        public string Role { get; set; } = "body";
    }
    public sealed class PositionedImage
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public byte[] Png { get; set; } = new byte[0];
        public string Role { get; set; } = "signature";
    }
    public sealed class ReportPage
    {
        public List<PositionedText> Text { get; set; } = new List<PositionedText>();
        public List<PositionedImage> Images { get; set; } = new List<PositionedImage>();
    }
    public sealed class PagePlan
    {
        public double Width { get; set; } = 595.2755905511812;
        public double Height { get; set; } = 841.8897637795277;
        public double Top { get; set; }
        public double Bottom { get; set; }
        public double Left { get; set; }
        public double Right { get; set; }
        public string EngineVersion { get; set; } = "ak-layout-1";
        public string FontFamily { get; set; } = "Noto Sans";
        public List<ReportPage> Pages { get; set; } = new List<ReportPage>();
    }
    public sealed class GeneratedDocumentInfo
    {
        public Guid Id { get; set; }
        public Guid RevisionId { get; set; }
        public string Format { get; set; } = "pdf";
        public string Sha256 { get; set; } = "";
        public int PageCount { get; set; }
        public string EngineVersion { get; set; } = "";
    }
}
