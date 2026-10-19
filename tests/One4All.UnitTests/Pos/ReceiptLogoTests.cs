using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Receipt logos: dots packed into printer rows and sent as a GS v 0 raster above the receipt text.</summary>
public sealed class ReceiptLogoTests
{
    [Fact]
    public void Dots_pack_most_significant_bit_first_and_print_as_a_raster()
    {
        bool[,] dark = new bool[2, 10];
        dark[0, 0] = true;
        dark[0, 9] = true;
        dark[1, 7] = true;
        ReceiptLogo logo = ReceiptLogo.FromDots(dark);
        (logo.WidthBytes, logo.Height).Should().Be((2, 2));
        logo.Raster.Should().Equal(0x80, 0x40, 0x01, 0x00);

        byte[] bytes = EscPosBuilder.Receipt("Hello", openDrawer: false, logo);
        int at = IndexOf(bytes, [0x1D, (byte)'v', (byte)'0', 0, 2, 0, 2, 0]);
        at.Should().BeGreaterThan(0);
        bytes.Skip(at + 8).Take(4).Should().Equal(0x80, 0x40, 0x01, 0x00);
        IndexOf(bytes, System.Text.Encoding.Latin1.GetBytes("Hello")).Should().BeGreaterThan(at, "the logo prints above the text");
        EscPosBuilder.Receipt("Hello", false).Should().Equal(EscPosBuilder.Receipt("Hello", false, null));
    }

    [Fact]
    public void Logos_fit_the_paper()
        => (ReceiptLogo.MaxDots(80), ReceiptLogo.MaxDots(58)).Should().Be((576, 384));

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
