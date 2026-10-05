using System;
using System.IO;
using System.Security.Cryptography;
using AkReporting.Contracts;

namespace AkReporting.Desktop
{
    public static class SafeExport
    {
        public static void Write(string destination, byte[] bytes, GeneratedDocumentInfo info)
        {
            if (!string.Equals(Path.GetExtension(destination), "." + info.Format, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Export extension must match the generated document format.");
            using (var sha = SHA256.Create())
                if (!string.Equals(BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", ""), info.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("Downloaded document hash mismatch.");
            var full = Path.GetFullPath(destination);
            var parent = Path.GetDirectoryName(full)!;
            if (!Directory.Exists(parent)) throw new DirectoryNotFoundException("Select an existing protected destination folder.");
            var temporary = Path.Combine(parent, ".akreport-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
                if (File.Exists(full)) File.Replace(temporary, full, null); else File.Move(temporary, full);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
