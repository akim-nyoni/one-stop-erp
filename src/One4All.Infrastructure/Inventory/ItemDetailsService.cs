using Microsoft.EntityFrameworkCore;
using One4All.Application.Abstractions.Inventory;
using One4All.Core.Common;
using One4All.Core.Inventory;
using One4All.Infrastructure.Persistence;

namespace One4All.Infrastructure.Inventory;

/// <summary>Keeps items' long descriptions, dimensions and pictures.</summary>
public sealed class ItemDetailsService : IItemDetailsService
{
    private const int MaxImageBytes = 1024 * 1024;
    private static readonly string[] ImageTypes = ["image/jpeg", "image/png", "image/gif", "image/webp"];

    private readonly One4AllDbContext _db;

    /// <summary>Creates the service.</summary>
    /// <param name="db">The database context.</param>
    public ItemDetailsService(One4AllDbContext db) => _db = db;

    /// <inheritdoc />
    public async Task<Result> SetDetailsAsync(long companyId, string itemCode, ItemDetails details, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(details);
        if (await ItemAsync(companyId, itemCode, ct).ConfigureAwait(false) is not Item item)
        {
            return Result.Failure(NoItem(itemCode));
        }

        if (details.LengthCm is < 0m || details.WidthCm is < 0m || details.HeightCm is < 0m || details.WeightKg is < 0m)
        {
            return Result.Failure(Error.Validation("Item.Dimensions", "Dimensions and weight cannot be negative."));
        }

        if (details.LongDescription is string text)
        {
            item.LongDescription = string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        item.LengthCm = details.LengthCm ?? item.LengthCm;
        item.WidthCm = details.WidthCm ?? item.WidthCm;
        item.HeightCm = details.HeightCm ?? item.HeightCm;
        item.UnitWeightKg = details.WeightKg ?? item.UnitWeightKg;
        if (item.LengthCm is decimal l && item.WidthCm is decimal w && item.HeightCm is decimal h && details is { LengthCm: not null } or { WidthCm: not null } or { HeightCm: not null })
        {
            // The volume follows the dimensions (cm³ to m³), for landed costs shared by volume.
            item.UnitVolumeM3 = decimal.Round(l * w * h / 1_000_000m, 6, MidpointRounding.AwayFromZero);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> SetImageAsync(long companyId, string itemCode, byte[]? image, string? contentType, CancellationToken ct = default)
    {
        if (await ItemAsync(companyId, itemCode, ct).ConfigureAwait(false) is not Item item)
        {
            return Result.Failure(NoItem(itemCode));
        }

        string? type = contentType?.Trim().ToLowerInvariant();
        if (image is not null && (image.Length == 0 || image.Length > MaxImageBytes || type is null || !ImageTypes.Contains(type)))
        {
            return Result.Failure(Error.Validation("Item.Image", "An item picture is a JPEG, PNG, GIF or WebP image of at most 1 MB."));
        }

        ItemImage? existing = await _db.Set<ItemImage>().FirstOrDefaultAsync(i => i.CompanyId == companyId && i.ItemId == item.Id, ct).ConfigureAwait(false);
        if (image is null)
        {
            if (existing is not null)
            {
                _db.Remove(existing);
            }
        }
        else if (existing is null)
        {
            _db.Set<ItemImage>().Add(new ItemImage(companyId, item.Id, image, type!));
        }
        else
        {
            existing.Content = image;
            existing.ContentType = type!;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<(byte[] Content, string ContentType)?> ImageAsync(long companyId, string itemCode, CancellationToken ct = default)
    {
        string code = (itemCode ?? string.Empty).Trim().ToUpperInvariant();
        var image = await _db.Set<ItemImage>().AsNoTracking()
            .Where(i => i.CompanyId == companyId && _db.Items.Any(x => x.Id == i.ItemId && x.Code == code)).Select(i => new { i.Content, i.ContentType }).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return image is null ? null : (image.Content, image.ContentType);
    }

    private static Error NoItem(string code) => Error.NotFound("Item.NotFound", $"Item {code} not found.");

    private Task<Item?> ItemAsync(long companyId, string itemCode, CancellationToken ct)
    {
        string code = (itemCode ?? string.Empty).Trim().ToUpperInvariant();
        return _db.Items.FirstOrDefaultAsync(i => i.CompanyId == companyId && i.Code == code, ct);
    }
}
