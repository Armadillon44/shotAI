using System.IO.Compression;
using System.Text;

namespace ShotAI.Core.Tests.Store;

/// <summary>Hand-made zips for the restore rules: names exactly as given, attributes, and flags.</summary>
internal static class ZipFixture
{
    /// <summary>One entry: its name, stored as is, its bytes, and optional external attributes.</summary>
    public sealed record Entry(string Name, byte[] Data, int? Attributes = null)
    {
        public Entry(string name, string text, int? attributes = null) : this(name, Encoding.UTF8.GetBytes(text), attributes) { }
    }

    public static void Write(string path, params Entry[] entries)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create, Encoding.UTF8);
        foreach (var e in entries)
        {
            var entry = zip.CreateEntry(e.Name, CompressionLevel.Optimal);
            if (e.Attributes is { } attributes) entry.ExternalAttributes = attributes;
            using var stream = entry.Open();
            stream.Write(e.Data);
        }
    }

    /// <summary>Replaces one entry's bytes in place, as damage between the write and the check would.</summary>
    public static void Replace(string path, string name, byte[] data)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.GetEntry(name)!.Delete();
        using var stream = zip.CreateEntry(name).Open();
        stream.Write(data);
    }

    public static void Remove(string path, string name)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        zip.GetEntry(name)!.Delete();
    }

    /// <summary>Every entry name, folder entries included, sorted.</summary>
    public static string[] Names(string path) => [.. InZipOrder(path).Order(StringComparer.Ordinal)];

    /// <summary>Every entry name in the order the central directory lists it.</summary>
    public static string[] InZipOrder(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Select(e => e.FullName).ToArray();
    }

    /// <summary>
    /// Clears the language-encoding flag (bit 11) in every local and central header, so a
    /// UTF-8 name reads as undeclared, as some zip tools write it.
    /// </summary>
    public static void ClearUtf8Flags(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (var i = 0; i + 10 <= bytes.Length; i++)
        {
            var signature = BitConverter.ToUInt32(bytes, i);
            if (signature == 0x04034b50) bytes[i + 7] &= 0xF7;
            else if (signature == 0x02014b50) bytes[i + 9] &= 0xF7;
        }
        File.WriteAllBytes(path, bytes);
    }
}
