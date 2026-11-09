using DareuMouse.Core;

namespace DareuMouse.Tests;

public class MouseTests
{
    private static MouseService ServiceFor(params SimulatedMouse[] mice) => new(() => mice);

    [Fact]
    public void Reads_the_state_of_an_awake_mouse()
    {
        var state = ServiceFor(new SimulatedMouse { BatteryStatus = 1 }).ReadState();
        Assert.Equal(MouseStatus.Ok, state.Status);
        Assert.Equal(40, state.Battery);
        Assert.True(state.Charging);
        Assert.Equal(2000, state.Polling);
        Assert.Equal([400, 800, 1600, 3200, 6400], state.Stages);
        Assert.Equal(1, state.ActiveStage);
        Assert.False(state.Wired);
    }

    [Fact]
    public void Ignores_events_and_stale_replies()
    {
        var state = ServiceFor(new SimulatedMouse { Noisy = true }).ReadState();
        Assert.Equal(MouseStatus.Ok, state.Status);
        Assert.Equal([400, 800, 1600, 3200, 6400], state.Stages);
    }

    [Fact]
    public void Retries_once_after_a_lost_packet()
    {
        var mouse = new SimulatedMouse { DropNext = 1 };
        Assert.Equal(MouseStatus.Ok, new MouseService(() => [mouse]).ReadState().Status);
    }

    [Fact]
    public void Reports_asleep_without_writing()
    {
        var mouse = new SimulatedMouse { Online = false };
        var before = mouse.Memory.ToArray();
        var state = new MouseService(() => [mouse]).SetPolling(1000);
        Assert.Equal(MouseStatus.Asleep, state.Status);
        Assert.Equal(before, mouse.Memory);
        Assert.DoesNotContain(mouse.Writes, w => w[0] == Protocol.CmdWrite);
    }

    [Fact]
    public void Reports_no_device()
    {
        Assert.Equal(MouseStatus.NoDevice, ServiceFor().ReadState().Status);
    }

    [Fact]
    public void Changes_polling_and_confirms_it()
    {
        var mouse = new SimulatedMouse();
        var state = new MouseService(() => [mouse]).SetPolling(4000);
        Assert.Equal(4000, state.Polling);
        Assert.Null(state.Error);
        Assert.Equal([0x20, Protocol.Checksum([0x20, 0])], mouse.Memory[..2]);
    }

    [Fact]
    public void Reports_when_the_mouse_keeps_its_polling()
    {
        var mouse = new SimulatedMouse { IgnorePollingWrites = true };
        var state = new MouseService(() => [mouse]).SetPolling(500);
        Assert.Equal(MouseStatus.Ok, state.Status);
        Assert.Equal(2000, state.Polling);
        Assert.NotNull(state.Error);
    }

    [Fact]
    public void Wired_mouse_refuses_2000_hz_without_writing()
    {
        var mouse = new SimulatedMouse(polling: 1000) { ProductId = Protocol.WiredProductId };
        var state = new MouseService(() => [mouse]).SetPolling(2000);
        Assert.True(state.Wired);
        Assert.Equal(1000, state.Polling);
        Assert.NotNull(state.Error);
        Assert.DoesNotContain(mouse.Writes, w => w[0] == Protocol.CmdWrite);
    }

    [Fact]
    public void Changes_the_active_stage()
    {
        var mouse = new SimulatedMouse();
        var state = new MouseService(() => [mouse]).SetActiveStage(3);
        Assert.Equal(3, state.ActiveStage);
    }

    [Fact]
    public void Edits_a_stage_without_touching_the_others()
    {
        var mouse = new SimulatedMouse();
        var state = new MouseService(() => [mouse]).SetStageDpi(4, 25650);
        Assert.Equal([400, 800, 1600, 3200, 25650], state.Stages);
        Assert.Equal(1, state.ActiveStage);
    }

    [Fact]
    public void Rejects_out_of_range_dpi_without_writing()
    {
        var mouse = new SimulatedMouse();
        var state = new MouseService(() => [mouse]).SetStageDpi(0, 30000);
        Assert.NotNull(state.Error);
        Assert.DoesNotContain(mouse.Writes, w => w[0] == Protocol.CmdWrite);
    }

    [Fact]
    public void Prefers_the_cable_and_closes_every_channel()
    {
        var receiver = new SimulatedMouse { Online = false };
        var wired = new SimulatedMouse(polling: 1000) { ProductId = Protocol.WiredProductId };
        var state = ServiceFor(receiver, wired).ReadState();
        Assert.True(state.Wired);
        Assert.Empty(receiver.Writes);
        Assert.True(receiver.Disposed && wired.Disposed);
    }

    [Fact]
    public void Falls_back_to_the_receiver_when_the_cable_only_charges()
    {
        var receiver = new SimulatedMouse();
        var wired = new SimulatedMouse(polling: 1000) { ProductId = Protocol.WiredProductId, Online = false };
        var state = ServiceFor(receiver, wired).ReadState();
        Assert.Equal(MouseStatus.Ok, state.Status);
        Assert.False(state.Wired);
    }

    [Fact]
    public void Rejects_a_corrupt_dpi_record()
    {
        var mouse = new SimulatedMouse();
        mouse.Memory[Protocol.AddrDpiStages + 3] ^= 0xFF;
        var state = new MouseService(() => [mouse]).ReadState();
        Assert.Equal(MouseStatus.Error, state.Status);
    }
}
