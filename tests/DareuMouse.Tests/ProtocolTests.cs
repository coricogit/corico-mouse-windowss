using DareuMouse.Core;

namespace DareuMouse.Tests;

public class ProtocolTests
{
    [Fact]
    public void Frames_include_report_id_in_checksum()
    {
        var active = Protocol.CheckActiveRequest();
        var battery = Protocol.BatteryRequest();
        Assert.Equal(16, active.Length);
        Assert.Equal((3, 74), (active[0], active[15]));
        Assert.Equal((4, 73), (battery[0], battery[15]));
        Assert.Equal(84, Protocol.Checksum([1, 0]));
        Assert.Equal([100, 241], Protocol.WithChecksum(100));
    }

    [Fact]
    public void Reads_are_split_into_ten_byte_chunks()
    {
        var requests = Protocol.ReadRequests(0x0FFF, 25).ToList();
        Assert.Equal(
            [[8, 0x0F, 0xFF, 10], [8, 0x10, 0x09, 10], [8, 0x10, 0x13, 5]],
            requests.Select(r => new[] { r[0], r[2], r[3], r[4] }).ToArray());
    }

    [Fact]
    public void Read_replies_must_echo_address_and_length()
    {
        var request = Protocol.ReadRequest(12, 4);
        var reply = new byte[16];
        reply[0] = 8; reply[3] = 12; reply[4] = 4;
        reply[5] = 1; reply[6] = 2; reply[7] = 3; reply[8] = 4;
        Assert.Equal([1, 2, 3, 4], Protocol.DecodeRead(request, reply));
        reply[3] = 13;
        Assert.Null(Protocol.DecodeRead(request, reply));
        reply[3] = 12; reply[0] = Protocol.CmdEvent;
        Assert.Null(Protocol.DecodeRead(request, reply));
    }

    [Fact]
    public void Writes_carry_address_length_and_data()
    {
        var request = Protocol.WriteRequest(0x0102, [9, 8, 7]);
        Assert.Equal(new byte[] { 7, 0, 0x01, 0x02, 3, 9, 8, 7 }, request[..8]);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(800, 15)]
    [InlineData(12800, 255)]
    [InlineData(12850, 0x4400)]
    [InlineData(25600, 0x44FF)]
    [InlineData(25650, 0x8800)]
    [InlineData(26000, 0x8807)]
    public void Dpi_ids_use_the_vendor_banks(int dpi, int id)
    {
        Assert.Equal(id, Protocol.DpiId(dpi));
        Assert.Equal(dpi, Protocol.DpiFromId(id));
    }

    [Fact]
    public void Every_valid_dpi_round_trips_through_a_stage_record()
    {
        for (var dpi = Protocol.DpiMin; dpi <= Protocol.DpiMax; dpi += Protocol.DpiStep)
            Assert.Equal(dpi, Protocol.DecodeStage(Protocol.EncodeStage(dpi)));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(125)]
    [InlineData(26050)]
    public void Invalid_dpi_is_rejected(int dpi) => Assert.Null(Protocol.DpiId(dpi));

    [Fact]
    public void Wired_mode_stops_at_1000_hz()
    {
        Assert.Equal([125, 250, 500, 1000], Protocol.PollingRates(wired: true));
        Assert.Equal([125, 250, 500, 1000, 2000, 4000], Protocol.PollingRates(wired: false));
        Assert.Null(Protocol.PollingCode(2000, wired: true));
        Assert.Equal((byte)0x20, Protocol.PollingCode(4000, wired: false));
    }

    [Theory]
    [InlineData(820, 800)]
    [InlineData(830, 850)]
    [InlineData(10, 100)]
    [InlineData(99999, 26000)]
    public void Clamp_snaps_to_steps(int input, int expected) => Assert.Equal(expected, Protocol.ClampDpi(input));
}
