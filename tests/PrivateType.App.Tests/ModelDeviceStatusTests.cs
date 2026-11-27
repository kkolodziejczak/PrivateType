using PrivateType.App;
using PrivateType.Core;
using Xunit;

namespace PrivateType.App.Tests;

public sealed class ModelDeviceStatusTests
{
    private static readonly EngineDevice Card = new("vulkan0", "NVIDIA GeForce MX450", EngineDeviceKind.GraphicsCard);
    private static readonly EngineDevice Integrated = new("vulkan1", "Intel UHD", EngineDeviceKind.IntegratedGraphics);

    [Fact]
    public void Names_the_graphics_card_the_in_process_engine_runs_on()
    {
        var status = ModelDeviceStatus.OfInProcess("GPU: NVIDIA GeForce MX450 (D3D12)", usingGpu: true, predicted: Card);

        Assert.Equal(ModelDeviceState.Running, status.State);
        Assert.Equal("Now running on: NVIDIA GeForce MX450 graphics card", status.Text);
    }

    [Fact]
    public void Says_processor_when_the_in_process_engine_runs_on_it()
    {
        var status = ModelDeviceStatus.OfInProcess("CPU (Avx2)", usingGpu: false, predicted: Card);

        Assert.Equal("Now running on: processor", status.Text);
    }

    [Fact]
    public void Predicts_the_processor_where_only_integrated_graphics_would_be_found()
    {
        var status = ModelDeviceStatus.OfInProcess(backend: null, usingGpu: false, predicted: Integrated);

        Assert.Equal(ModelDeviceState.NotLoaded, status.State);
        Assert.Equal("Not loaded — will use processor", status.Text);
    }

    [Fact]
    public void Predicts_the_graphics_card_before_the_in_process_engine_loads()
    {
        var status = ModelDeviceStatus.OfInProcess(backend: null, usingGpu: false, predicted: Card);

        Assert.Equal("Not loaded — will use NVIDIA GeForce MX450 graphics card", status.Text);
    }
}
