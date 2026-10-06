using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using One4All.Application.Abstractions.Pos;
using One4All.Application.Pos;
using One4All.Infrastructure.Pos;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>
/// Covers the peripherals without hardware: the provider builds what the settings ask for (nothing, simulators, network devices), a network
/// printer receives the exact ESC/POS bytes and the drawer kick goes through it, a network scale is asked for and reports the weight, an
/// unreachable printer says so instead of throwing, and the simulated card terminal approves within its limit.
/// </summary>
public sealed class PosDeviceTests
{
    [Fact]
    public async Task The_provider_builds_simulators_or_nothing()
    {
        DeviceProvider none = new(new PosDeviceOptions());
        (none.Printer, none.Drawer, none.Scale, none.Display, none.CardTerminal, none.LabelPrinter).Should().Be((null, null, null, null, null, null));
        none.BarcodeRules.Should().BeEquivalentTo(Barcodes.DefaultRules);

        DeviceProvider sim = new(new PosDeviceOptions
        {
            Printer = new DeviceSettings { Connection = DeviceConnection.Simulator }, Scale = new DeviceSettings { Connection = DeviceConnection.Simulator },
            Display = new DeviceSettings { Connection = DeviceConnection.Simulator }, CardTerminal = new DeviceSettings { Connection = DeviceConnection.Simulator },
            SimulatedWeight = 0.750m, SimulatedCardLimit = 100m,
        });
        (await sim.Scale!.ReadAsync()).Should().Be(new ScaleReading(0.750m, true));
        (await sim.Drawer!.OpenAsync()).Should().BeNull();
        ((SimulatedPrinter)sim.Printer!).Jobs.Single().TakeLast(5).Should().Equal(0x1B, (byte)'p', 0, 25, 250);
        await sim.Display!.ShowAsync("Total", "9.99");
        ((SimulatedDisplay)sim.Display).Showing.Should().Be(("Total", "9.99"));
        (await sim.CardTerminal!.ChargeAsync(99m, "S1")).Approved.Should().BeTrue();
        (await sim.CardTerminal.ChargeAsync(150m, "S2")).Should().Match<CardAuthorisation>(a => !a.Approved && a.AuthorisationCode.Contains("Declined"));
    }

    [Fact]
    public async Task A_network_printer_gets_the_bytes_and_a_scale_answers()
    {
        using TcpListener printerListener = new(IPAddress.Loopback, 0);
        printerListener.Start();
        int printerPort = ((IPEndPoint)printerListener.LocalEndpoint).Port;
        Task<byte[]> received = Task.Run(async () =>
        {
            using TcpClient client = await printerListener.AcceptTcpClientAsync();
            using MemoryStream buffer = new();
            await client.GetStream().CopyToAsync(buffer);
            return buffer.ToArray();
        });
        byte[] receipt = EscPosBuilder.Receipt("TEST RECEIPT", openDrawer: false);
        IReceiptPrinter printer = new DeviceProvider(new PosDeviceOptions { Printer = new DeviceSettings { Connection = DeviceConnection.Network, Host = "127.0.0.1", Port = printerPort } }).Printer!;
        (await printer.PrintAsync(receipt)).Should().BeNull();
        (await received).Should().Equal(receipt);

        using TcpListener scaleListener = new(IPAddress.Loopback, 0);
        scaleListener.Start();
        int scalePort = ((IPEndPoint)scaleListener.LocalEndpoint).Port;
        Task<string> asked = Task.Run(async () =>
        {
            using TcpClient client = await scaleListener.AcceptTcpClientAsync();
            NetworkStream stream = client.GetStream();
            byte[] request = new byte[16];
            int read = await stream.ReadAsync(request);
            await stream.WriteAsync("ST,GS,  2.345kg\r\n"u8.ToArray());
            return Encoding.ASCII.GetString(request, 0, read);
        });
        IScale scale = new DeviceProvider(new PosDeviceOptions { Scale = new DeviceSettings { Connection = DeviceConnection.Network, Host = "127.0.0.1", Port = scalePort } }).Scale!;
        (await scale.ReadAsync()).Should().Be(new ScaleReading(2.345m, true));
        (await asked).Should().Be("W\r\n");
    }

    [Fact]
    public async Task An_unreachable_printer_reports_rather_than_throws()
    {
        int closedPort;
        using (TcpListener probe = new(IPAddress.Loopback, 0))
        {
            probe.Start();
            closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        IReceiptPrinter printer = new NetworkRawPrinter(new DeviceSettings { Host = "127.0.0.1", Port = closedPort, TimeoutMs = 1000 });
        (await printer.PrintAsync([1, 2, 3])).Should().Contain("not reachable");
        NetworkCustomerDisplay.Frame("Total", "12.50").Should().StartWith([0x0C]).And.HaveCount(1 + 20 + 2 + 20);
        DisplayText.Amount("Change", 2.5m).Should().Be("Change".PadRight(16) + "2.50");
    }
}
