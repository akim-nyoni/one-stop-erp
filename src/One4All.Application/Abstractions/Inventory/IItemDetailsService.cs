using One4All.Core.Common;

namespace One4All.Application.Abstractions.Inventory;

/// <summary>An item's catalogue details: the long description, the dimensions and the picture (brief §4.6 item master).</summary>
public interface IItemDetailsService
{
    /// <summary>Sets the long description and the dimensions (null leaves a value; a blank description clears it).</summary>
    Task<Result> SetDetailsAsync(long companyId, string itemCode, ItemDetails details, CancellationToken ct = default);

    /// <summary>Sets (or, with null, removes) the item's picture: JPEG, PNG, GIF or WebP, at most 1 MB.</summary>
    Task<Result> SetImageAsync(long companyId, string itemCode, byte[]? image, string? contentType, CancellationToken ct = default);

    /// <summary>The item's picture, or null when it has none.</summary>
    Task<(byte[] Content, string ContentType)?> ImageAsync(long companyId, string itemCode, CancellationToken ct = default);
}

/// <summary>Catalogue details to set.</summary>
public sealed record ItemDetails
{
    /// <summary>The long description.</summary>
    public string? LongDescription { get; init; }

    /// <summary>Length in centimetres.</summary>
    public decimal? LengthCm { get; init; }

    /// <summary>Width in centimetres.</summary>
    public decimal? WidthCm { get; init; }

    /// <summary>Height in centimetres.</summary>
    public decimal? HeightCm { get; init; }

    /// <summary>Weight of one unit in kilograms.</summary>
    public decimal? WeightKg { get; init; }
}
