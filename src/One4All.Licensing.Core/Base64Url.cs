namespace One4All.Licensing.Core;

/// <summary>URL-safe, unpadded Base64 encoding used for the compact licence token.</summary>
public static class Base64Url
{
    /// <summary>Encodes bytes as URL-safe Base64 without padding.</summary>
    /// <param name="bytes">The bytes to encode.</param>
    /// <returns>The encoded string.</returns>
    public static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Decodes a URL-safe, unpadded Base64 string.</summary>
    /// <param name="value">The encoded string.</param>
    /// <returns>The decoded bytes.</returns>
    public static byte[] Decode(string value)
    {
        string padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }
}
