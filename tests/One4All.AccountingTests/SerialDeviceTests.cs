using FluentAssertions;
using One4All.Infrastructure.Pos;
using Xunit;

namespace One4All.AccountingTests;

/// <summary>Covers serial peripherals: the provider builds them from Serial settings, and a missing port leaves the till working (no weight, a message, a dark display).</summary>
public sealed class SerialDeviceTests
{
    [Fact]
    public async Task Serial_devices_are_built_and_a_missing_port_does_not_stop_the_sale()
    {
        DeviceSettings missing = new() { Connection = DeviceConnection.Serial, PortName = "COM249", BaudRate = 9600, TimeoutMs = 200 };
        DeviceProvider devices = new(new PosDeviceOptions { Printer = missing, Scale = missing, Display = missing, LabelPrinter = missing });
        devices.Printer.Should().BeOfType<SerialRawPrinter>();
        devices.Drawer.Should().NotBeNull("the drawer opens through the serial printer");
        devices.Scale.Should().BeOfType<SerialScale>();
        devices.Display.Should().BeOfType<SerialCustomerDisplay>();
        devices.LabelPrinter.Should().BeOfType<SerialRawPrinter>();

        (await devices.Scale!.ReadAsync()).Should().BeNull();
        (await devices.Printer!.PrintAsync([0x1B, 0x40])).Should().Contain("COM249");
        await devices.Display!.Invoking(d => d.ShowAsync("Total", "12.50")).Should().NotThrowAsync();
    }
}
