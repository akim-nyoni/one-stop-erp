using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using One4All.Application.Abstractions.Inventory;
using One4All.Application.Abstractions.Municipal;
using One4All.Core.Inventory;
using One4All.Core.Municipal;
using One4All.Core.Trading;
using One4All.Infrastructure.Persistence;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers item pictures, long descriptions and dimensions, stand GPS and suburb, and meter type, size and location.</summary>
[Collection(AccountingCollection.Name)]
public sealed class RegisterDetailsTests
{
    private readonly AccountingFixture _fixture;

    public RegisterDetailsTests(AccountingFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task Items_stands_and_meters_carry_their_details()
    {
        Skip.IfNot(_fixture.Available, _fixture.SkipReason);
        await using ServiceProvider provider = _fixture.BuildServiceProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        One4AllDbContext db = scope.ServiceProvider.GetRequiredService<One4AllDbContext>();
        long companyId = await Scenario.SeedAsync(db);
        Item box = new(companyId, "RD-BOX", "Storage box");
        Customer owner = new(companyId, "RD-OWN", "Stand owner", Scenario.Usd);
        db.AddRange(box, owner);
        await db.SaveChangesAsync();
        Property stand = new(companyId, "RD-77", "Ward 3", owner.Id);
        Meter meter = new(companyId, $"RD-M-{companyId}");
        db.AddRange(stand, meter);
        await db.SaveChangesAsync();

        IItemDetailsService items = scope.ServiceProvider.GetRequiredService<IItemDetailsService>();
        (await items.SetDetailsAsync(companyId, "rd-box", new ItemDetails { LongDescription = "Stackable 40 litre box with lid", LengthCm = 50m, WidthCm = 40m, HeightCm = 30m, WeightKg = 1.2m }))
            .IsSuccess.Should().BeTrue();
        (await items.SetDetailsAsync(companyId, "RD-BOX", new ItemDetails { WidthCm = -1m })).Error.Code.Should().Be("Item.Dimensions");
        (await items.SetImageAsync(companyId, "RD-BOX", [1, 2], "image/tiff")).Error.Code.Should().Be("Item.Image");
        (await items.SetImageAsync(companyId, "RD-BOX", [0x89, 0x50, 0x4E, 0x47], "image/png")).IsSuccess.Should().BeTrue();
        (await items.ImageAsync(companyId, "RD-BOX"))!.Value.ContentType.Should().Be("image/png");
        Item saved = await db.Items.AsNoTracking().SingleAsync(i => i.Id == box.Id);
        (saved.LongDescription, saved.UnitVolumeM3, saved.UnitWeightKg).Should().Be(("Stackable 40 litre box with lid", (decimal?)0.06m, (decimal?)1.2m));
        (await items.SetImageAsync(companyId, "RD-BOX", null, null)).IsSuccess.Should().BeTrue();
        (await items.ImageAsync(companyId, "RD-BOX")).Should().BeNull();

        IPropertyRegisterService register = scope.ServiceProvider.GetRequiredService<IPropertyRegisterService>();
        (await register.SetLocationAsync(companyId, "RD-77", "Belvedere", -17.8312m, null)).Error.Code.Should().Be("Mun.Gps");
        (await register.SetLocationAsync(companyId, "rd-77", "Belvedere", -17.8312m, 31.0207m)).IsSuccess.Should().BeTrue();
        (await register.SetMeterDetailsAsync(companyId, meter.Serial, "Water", 0, null)).Error.Code.Should().Be("Mun.MeterSize");
        (await register.SetMeterDetailsAsync(companyId, meter.Serial, "Water", 20, "Front boundary, left of gate")).IsSuccess.Should().BeTrue();

        Property located = await db.Properties.AsNoTracking().SingleAsync(p => p.Id == stand.Id);
        (located.Suburb, located.Latitude, located.Longitude).Should().Be(("Belvedere", (decimal?)-17.8312m, (decimal?)31.0207m));
        Meter described = await db.Set<Meter>().AsNoTracking().SingleAsync(m => m.Id == meter.Id);
        (described.MeterType, described.SizeMm, described.Location).Should().Be(("Water", (int?)20, "Front boundary, left of gate"));
    }
}
