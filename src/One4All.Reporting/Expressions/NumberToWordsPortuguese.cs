using System.Text;

namespace One4All.Reporting.Expressions;

/// <summary>
/// Numbers and amounts in Portuguese words (European usage, as in Mozambique and Angola; brief §13.1 multi-language cheques): "cento e quinze
/// meticais e cinco centavos", "mil e duzentos", "dois milhões de meticais". Pure.
/// </summary>
public static class NumberToWordsPortuguese
{
    private static readonly string[] Units =
    [
        "zero", "um", "dois", "três", "quatro", "cinco", "seis", "sete", "oito", "nove", "dez",
        "onze", "doze", "treze", "catorze", "quinze", "dezasseis", "dezassete", "dezoito", "dezanove",
    ];

    private static readonly string[] Tens = ["", "", "vinte", "trinta", "quarenta", "cinquenta", "sessenta", "setenta", "oitenta", "noventa"];

    private static readonly string[] Hundreds = ["", "cento", "duzentos", "trezentos", "quatrocentos", "quinhentos", "seiscentos", "setecentos", "oitocentos", "novecentos"];

    // Groups of three digits from the largest: (value, singular, plural); thousands are "mil" either way and "um" is dropped before it.
    private static readonly (long Value, string One, string Many)[] Scales =
    [
        (1_000_000_000, "mil milhões", "mil milhões"),
        (1_000_000, "milhão", "milhões"),
        (1_000, "mil", "mil"),
    ];

    /// <summary>A whole number in words.</summary>
    /// <param name="value">The number.</param>
    /// <returns>Its words.</returns>
    public static string Convert(long value)
    {
        if (value == 0)
        {
            return Units[0];
        }

        if (value < 0)
        {
            return "menos " + Convert(-value);
        }

        List<(string Words, int Group)> parts = [];
        long remaining = value;
        foreach ((long scale, string one, string many) in Scales)
        {
            if (remaining >= scale)
            {
                int count = (int)(remaining / scale);
                remaining %= scale;
                string words = scale == 1_000 && count == 1 ? one
                    : scale == 1_000_000_000 ? $"{(count == 1 ? string.Empty : Below1000(count) + " ")}{one}"
                    : $"{Below1000(count)} {(count == 1 ? one : many)}";
                parts.Add((words.Trim(), count));
            }
        }

        if (remaining > 0)
        {
            parts.Add((Below1000((int)remaining), (int)remaining));
        }

        // The last group is joined with "e" when it is below a hundred or a round hundred (mil e cem, mil e vinte, mil trezentos e dez).
        StringBuilder sb = new(parts[0].Words);
        for (int i = 1; i < parts.Count; i++)
        {
            bool last = i == parts.Count - 1;
            int group = parts[i].Group;
            sb.Append(last && (group < 100 || group % 100 == 0) ? " e " : " ").Append(parts[i].Words);
        }

        return sb.ToString();
    }

    /// <summary>An amount in words with its currency units: singular for one, "de" before the unit after a round million.</summary>
    /// <param name="amount">The amount.</param>
    /// <param name="majorSingular">The major unit, singular (metical).</param>
    /// <param name="majorPlural">The major unit, plural (meticais).</param>
    /// <param name="minorSingular">The minor unit, singular (centavo).</param>
    /// <param name="minorPlural">The minor unit, plural (centavos).</param>
    /// <returns>The amount in words, first letter capitalised.</returns>
    public static string Amount(decimal amount, string majorSingular, string majorPlural, string minorSingular, string minorPlural)
    {
        bool negative = amount < 0m;
        decimal abs = Math.Abs(amount);
        long whole = (long)decimal.Truncate(abs);
        int minor = (int)decimal.Round((abs - whole) * 100m, 0, MidpointRounding.ToEven);
        string major = whole == 1 ? majorSingular : majorPlural;
        string text = $"{Convert(whole)} {(whole >= 1_000_000 && whole % 1_000_000 == 0 ? "de " : string.Empty)}{major}";
        if (minor > 0)
        {
            text += $" e {Convert(minor)} {(minor == 1 ? minorSingular : minorPlural)}";
        }

        text = negative ? "menos " + text : text;
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Below1000(int value)
    {
        if (value == 100)
        {
            return "cem";
        }

        int hundreds = value / 100, rest = value % 100;
        string tail = rest == 0 ? string.Empty : rest < 20 ? Units[rest] : rest % 10 == 0 ? Tens[rest / 10] : $"{Tens[rest / 10]} e {Units[rest % 10]}";
        return hundreds == 0 ? tail : rest == 0 ? Hundreds[hundreds] : $"{Hundreds[hundreds]} e {tail}";
    }
}
