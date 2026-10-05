using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using One4All.Application.Documents;
using Xunit;

namespace One4All.UnitTests.Documents;

/// <summary>
/// Covers the text PDF writer: an open file carries the text and a valid cross-reference table; a protected file carries none of the
/// text in the clear, and an independent implementation of the Standard Security Handler (ISO 32000-1 algorithms 2 and 5) accepts the
/// right user password, rejects a wrong one, and decrypts the page back to the text.
/// </summary>
public sealed partial class PdfTextDocumentTests
{
    private static readonly byte[] Padding =
    [
        0x28, 0xBF, 0x4E, 0x5E, 0x4E, 0x75, 0x8A, 0x41, 0x64, 0x00, 0x4E, 0x56, 0xFF, 0xFA, 0x01, 0x08,
        0x2E, 0x2E, 0x00, 0xB6, 0xD0, 0x68, 0x3E, 0x80, 0x2F, 0x0C, 0xA9, 0xFE, 0x64, 0x53, 0x69, 0x7A,
    ];

    private const string Payslip = "ACME LTD\nPayslip 2026-09 (Pat)\nNet pay        1,234.50";

    [GeneratedRegex(@"/O <([0-9A-F]+)> /U <([0-9A-F]+)> /P (-?\d+)")]
    private static partial Regex EncryptValues();

    [GeneratedRegex(@"/ID \[<([0-9A-F]+)>")]
    private static partial Regex FileId();

    [GeneratedRegex(@"5 0 obj\n<< /Length (\d+) >>\nstream\n")]
    private static partial Regex FirstContent();

    [Fact]
    public void An_open_file_carries_the_text_and_a_consistent_xref()
    {
        byte[] pdf = PdfTextDocument.Build(Payslip);
        string text = Encoding.Latin1.GetString(pdf);

        text.Should().StartWith("%PDF-1.6").And.EndWith("%%EOF\n").And.Contain("(Net pay        1,234.50) Tj").And.NotContain("/Encrypt");
        int xref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0], System.Globalization.CultureInfo.InvariantCulture);
        text[xref..].Should().StartWith("xref\n0 6\n");
        foreach (Match entry in Regex.Matches(text[xref..], @"(\d{10}) 00000 n "))
        {
            int offset = int.Parse(entry.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            text[offset..].Should().MatchRegex(@"^\d+ 0 obj");
        }
    }

    [Fact]
    public void A_protected_file_opens_only_with_the_user_password()
    {
        byte[] pdf = PdfTextDocument.Build(Payslip, "19800412");
        string text = Encoding.Latin1.GetString(pdf);
        text.Should().Contain("/Filter /Standard /V 4 /R 4").And.Contain("/CFM /AESV2").And.NotContain("Net pay");

        Match values = EncryptValues().Match(text);
        byte[] owner = Convert.FromHexString(values.Groups[1].Value), user = Convert.FromHexString(values.Groups[2].Value), id = Convert.FromHexString(FileId().Match(text).Groups[1].Value);
        int permissions = int.Parse(values.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture);

        UserMatches(Key("19800412", owner, permissions, id), id, user).Should().BeTrue();
        UserMatches(Key("19800413", owner, permissions, id), id, user).Should().BeFalse();

        // Decrypt the first page with the key from the right password.
        Match content = FirstContent().Match(text);
        int length = int.Parse(content.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        byte[] stream = pdf[(content.Index + content.Length)..(content.Index + content.Length + length)];
        byte[] objectKey = MD5Hash([.. Key("19800412", owner, permissions, id), 5, 0, 0, 0, 0, (byte)'s', (byte)'A', (byte)'l', (byte)'T'])[..16];
        using Aes aes = Aes.Create();
        aes.Key = objectKey;
        Encoding.Latin1.GetString(aes.DecryptCbc(stream[16..], stream[..16], PaddingMode.PKCS7)).Should().Contain("(Net pay        1,234.50) Tj").And.Contain("(Payslip 2026-09 \\(Pat\\)) Tj");
    }

    [Fact]
    public void Long_text_runs_onto_more_pages()
    {
        string text = Encoding.Latin1.GetString(PdfTextDocument.Build(string.Join('\n', Enumerable.Range(1, 150).Select(i => $"Line {i}"))));
        text.Should().Contain("/Count 3").And.Contain("(Line 150) Tj");
    }

#pragma warning disable CA5351 // The PDF Standard Security Handler is defined with MD5 and RC4.
    private static byte[] MD5Hash(byte[] data) => MD5.HashData(data);

    private static byte[] Key(string password, byte[] owner, int permissions, byte[] id)
    {
        byte[] raw = Encoding.Latin1.GetBytes(password);
        byte[] padded = [.. raw.Take(32), .. Padding.Take(32 - Math.Min(raw.Length, 32))];
        byte[] hash = MD5.HashData([.. padded, .. owner, .. BitConverter.GetBytes(permissions), .. id]);
        for (int i = 0; i < 50; i++)
        {
            hash = MD5.HashData(hash[..16]);
        }

        return hash[..16];
    }

    private static bool UserMatches(byte[] key, byte[] id, byte[] user)
    {
        byte[] value = Rc4(key, MD5.HashData([.. Padding, .. id]));
        for (byte i = 1; i <= 19; i++)
        {
            value = Rc4([.. key.Select(b => (byte)(b ^ i))], value);
        }

        return value.SequenceEqual(user[..16]);
    }
#pragma warning restore CA5351

    private static byte[] Rc4(byte[] key, byte[] data)
    {
        byte[] s = [.. Enumerable.Range(0, 256).Select(i => (byte)i)];
        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + s[i] + key[i % key.Length]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
        }

        byte[] output = new byte[data.Length];
        for (int n = 0, i = 0, j = 0; n < data.Length; n++)
        {
            i = (i + 1) & 0xFF;
            j = (j + s[i]) & 0xFF;
            (s[i], s[j]) = (s[j], s[i]);
            output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
        }

        return output;
    }
}
