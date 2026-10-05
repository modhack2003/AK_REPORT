using System.Reflection;
using AkReporting.Domain;
using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace AkReporting.Rendering;

public sealed class ReportFonts : IFontResolver
{
    private static readonly object Gate = new();
    private static bool initialized;
    private static readonly Dictionary<string, byte[]> Files = new(StringComparer.Ordinal);
    public static void Initialize()
    {
        lock (Gate)
        {
            if (initialized) return;
            GlobalFontSettings.FontResolver = new ReportFonts();
            initialized = true;
        }
    }
    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (familyName is not ("Noto Sans" or "Noto Serif")) throw new ValidationException("Unsupported font family.");
        return new FontResolverInfo((familyName == "Noto Sans" ? "NotoSans-" : "NotoSerif-") + (bold && italic ? "BoldItalic" : bold ? "Bold" : italic ? "Italic" : "Regular"));
    }
    public byte[] GetFont(string faceName)
    {
        lock (Gate)
        {
            if (Files.TryGetValue(faceName, out var bytes)) return bytes;
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames().Single(x => x.EndsWith(faceName + ".ttf", StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            bytes = memory.ToArray();
            Files.Add(faceName, bytes);
            return bytes;
        }
    }
    public static XFont Font(double size, bool bold = false, bool italic = false, string family = "Noto Sans")
    {
        Initialize();
        return new XFont(family, size, (bold ? XFontStyleEx.Bold : XFontStyleEx.Regular) | (italic ? XFontStyleEx.Italic : XFontStyleEx.Regular));
    }
    public static void ValidateGlyphs(string text, XFont font)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is '\n' or '\r' or '\t') continue;
            if (GlyphHelper.GlyphIndexFromCodePoint(rune.Value, font) == 0)
                throw new ValidationException($"The pinned font does not support character U+{rune.Value:X4}. A reviewed font/shaping update is required.");
        }
    }
}
