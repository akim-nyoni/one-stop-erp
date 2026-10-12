using FluentAssertions;
using One4All.Application.Pos;
using Xunit;

namespace One4All.UnitTests.Pos;

/// <summary>Covers rearranging favourite tiles by dragging.</summary>
public sealed class FavouriteOrderTests
{
    [Fact]
    public void A_dragged_tile_takes_the_place_of_the_one_it_is_dropped_on()
    {
        FavouriteOrder.Move([1, 2, 3, 4], 4, 2).Should().Equal(1, 4, 2, 3);
        FavouriteOrder.Move([1, 2, 3, 4], 1, 3).Should().Equal(2, 3, 1, 4);
        FavouriteOrder.Move([1, 2, 3], 9, 2).Should().Equal(1, 2, 3);
        FavouriteOrder.Move([1, 2, 3], 2, 2).Should().Equal(1, 2, 3);
    }
}
