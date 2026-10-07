using PrivateType.Core;
using Xunit;

namespace PrivateType.Core.Tests;

public sealed class EngineDevicesTests
{
    // Trimmed from `nemo-speech --json doctor` on a laptop with Intel and NVIDIA graphics.
    private const string LaptopWithTwoGpus = """
        {
          "devices": [
            { "description": "Intel(R) UHD Graphics", "index": 0, "memory_free": 16229222400, "name": "Vulkan0", "type": "integrated-gpu" },
            { "description": "NVIDIA GeForce MX450", "index": 1, "memory_free": 1917945243, "name": "Vulkan1", "type": "gpu" },
            { "description": "11th Gen Intel(R) Core(TM) i7-11850H @ 2.50GHz", "index": 2, "memory_free": 13999312896, "name": "CPU", "type": "cpu" }
          ]
        }
        """;

    [Fact]
    public void Prefers_a_graphics_card_over_integrated_graphics_listed_first()
    {
        var device = EngineDevices.Choose(LaptopWithTwoGpus, ModelDevices.Automatic);

        Assert.Equal(new EngineDevice("vulkan:1", "NVIDIA GeForce MX450", EngineDeviceKind.GraphicsCard), device);
    }

    [Fact]
    public void Uses_integrated_graphics_when_the_graphics_card_cannot_hold_the_model()
    {
        var json = LaptopWithTwoGpus.Replace("1917945243", "536870912", StringComparison.Ordinal);

        var device = EngineDevices.Choose(json, ModelDevices.Automatic);

        Assert.Equal("vulkan:0", device.Argument);
        Assert.Equal(EngineDeviceKind.IntegratedGraphics, device.Kind);
    }

    [Fact]
    public void Uses_the_processor_when_asked_to()
    {
        Assert.Equal(EngineDevice.Processor, EngineDevices.Choose(LaptopWithTwoGpus, ModelDevices.Processor));
    }

    [Theory]
    [InlineData("""{ "devices": [ { "name": "CPU", "type": "cpu", "memory_free": 13999312896 } ] }""")]
    [InlineData("""{ "devices": [] }""")]
    [InlineData("not json")]
    [InlineData("")]
    public void Falls_back_to_the_processor_without_a_usable_graphics_device(string doctorJson)
    {
        Assert.Equal(EngineDevice.Processor, EngineDevices.Choose(doctorJson, ModelDevices.Automatic));
    }

    [Fact]
    public void Resets_an_unknown_model_device_setting_on_load()
    {
        var settings = PortableSettings.Default with { ModelDevice = "quantum" };

        Assert.NotNull(PortableSettingsValidator.Validate(settings));
        Assert.Null(PortableSettingsValidator.Validate(PortableSettings.Default));
    }
}
