using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using One4All.Application.Abstractions.Offline;
using One4All.Application.Abstractions.Pos;
using One4All.Infrastructure.Offline;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the terminal's encrypted local store (brief §6B): the protector seals and opens with a key kept (DPAPI-protected on Windows) beside
/// the store and refuses altered or foreign data; the offline catalogue snapshot round-trips sealed and is ignored when tampered with; queued
/// sales are written sealed, never as readable JSON, while entries written before encryption still flush; the snapshot finds a scanned code.
/// </summary>
public sealed class LocalEncryptedStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "one4all-local-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    [Fact]
    public void The_protector_round_trips_keeps_its_key_and_refuses_altered_or_foreign_data()
    {
        byte[] plain = Encoding.UTF8.GetBytes("till float 250.00");
        byte[] sealedData = new LocalDataProtector(_dir).Protect(plain);

        Encoding.UTF8.GetString(sealedData).Should().NotContain("till float");
        new LocalDataProtector(_dir).Unprotect(sealedData).Should().Equal(plain, "a new instance over the same folder reuses the saved key");

        byte[] altered = (byte[])sealedData.Clone();
        altered[^1] ^= 0x01;
        FluentActions.Invoking(() => new LocalDataProtector(_dir).Unprotect(altered)).Should().Throw<CryptographicException>();
        FluentActions.Invoking(() => new LocalDataProtector(Path.Combine(_dir, "elsewhere")).Unprotect(sealedData)).Should().Throw<CryptographicException>(
            "another key cannot open it");
    }

    [Fact]
    public async Task The_catalogue_snapshot_is_sealed_and_ignored_when_tampered_with()
    {
        LocalDataProtector protector = new(_dir);
        FileLocalCatalogueStore store = new(_dir, protector);
        (await store.LoadAsync()).Should().BeNull();

        PosOfflineSnapshot snapshot = new()
        {
            TakenUtc = DateTimeOffset.UtcNow, CompanyId = 7, BaseCurrency = "USD", WarehouseId = 3, TerminalId = 2, OpenSessionId = 41,
            TaxRates = new Dictionary<string, decimal> { ["VAT"] = 0.15m }, Tiles = [Tile(1, "COLA", "Cola 500ml", "6001234500011", "101", 1.50m)],
            Accounts = new Dictionary<string, string> { ["Cash"] = "1000" }, TenderCurrencies = [new OfflineTenderCurrency("ZWG", 26.5m)],
            Customers = [new OfflineCustomer(9, "C001", "Corner Shop")],
        };
        await store.SaveAsync(snapshot);

        string file = Directory.EnumerateFiles(_dir, "*.o4a").Single();
        Encoding.UTF8.GetString(await File.ReadAllBytesAsync(file)).Should().NotContain("Cola").And.NotContain("Corner Shop");

        PosOfflineSnapshot loaded = (await new FileLocalCatalogueStore(_dir, new LocalDataProtector(_dir)).LoadAsync())!;
        loaded.OpenSessionId.Should().Be(41);
        loaded.Tiles.Single().UnitPrice.Should().Be(1.50m);
        loaded.TaxRates["VAT"].Should().Be(0.15m);
        loaded.Customers.Single().Name.Should().Be("Corner Shop");
        loaded.TenderCurrencies.Single().Rate.Should().Be(26.5m);

        byte[] bytes = await File.ReadAllBytesAsync(file);
        bytes[^3] ^= 0x10;
        await File.WriteAllBytesAsync(file, bytes);
        (await store.LoadAsync()).Should().BeNull("an altered snapshot is not trusted");
    }

    [Fact]
    public async Task Queued_sales_are_sealed_and_plain_entries_from_before_still_flush()
    {
        string queueDir = Path.Combine(_dir, "queue");
        FileLocalOfflineQueue queue = new(queueDir, new LocalDataProtector(_dir));
        LocalOfflineEntry sealedEntry = new(Guid.NewGuid(), 1, "PosSale", "{\"SaleNumber\":\"S-SECRET\"}", DateTimeOffset.UtcNow);
        await queue.EnqueueAsync(sealedEntry);
        await queue.EnqueueAsync(sealedEntry); // once per operation id

        Directory.EnumerateFiles(queueDir).Should().ContainSingle(f => f.EndsWith(".o4a", StringComparison.Ordinal));
        Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Directory.EnumerateFiles(queueDir).Single())).Should().NotContain("S-SECRET");

        LocalOfflineEntry legacy = new(Guid.NewGuid(), 1, "PosSale", "{\"SaleNumber\":\"S-OLD\"}", DateTimeOffset.UtcNow.AddMinutes(-5));
        await new FileLocalOfflineQueue(queueDir).EnqueueAsync(legacy); // written plain, as before encryption

        (await queue.CountAsync()).Should().Be(2);
        (await queue.ListAsync()).Select(e => e.OperationId).Should().Equal(legacy.OperationId, sealedEntry.OperationId);

        await queue.RemoveAsync(legacy.OperationId);
        await queue.RemoveAsync(sealedEntry.OperationId);
        (await queue.CountAsync()).Should().Be(0);
    }

    [Fact]
    public void The_snapshot_finds_a_scanned_barcode_plu_or_code()
    {
        PosOfflineSnapshot snapshot = new() { Tiles = [Tile(1, "COLA", "Cola", "6001234500011", "101", 1m), Tile(2, "BREAD", "Bread", null, "202", 2m)] };

        snapshot.Find("6001234500011")!.ItemId.Should().Be(1);
        snapshot.Find("202")!.ItemId.Should().Be(2);
        snapshot.Find(" bread ")!.ItemId.Should().Be(2);
        snapshot.Find("MILK").Should().BeNull();
    }

    private static PosTile Tile(long id, string code, string name, string? barcode, string? plu, decimal price)
        => new(id, code, name, "Grocery", price, "4000", "VAT", barcode, plu);
}
