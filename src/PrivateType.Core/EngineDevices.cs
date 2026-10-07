using System.Text.Json;

namespace PrivateType.Core;

// Where the speech engine runs. Argument is the engine's --device value.
public sealed record EngineDevice(string Argument, string Description, EngineDeviceKind Kind)
{
    public static EngineDevice Processor { get; } = new("cpu", "processor", EngineDeviceKind.Processor);
}

public enum EngineDeviceKind
{
    GraphicsCard,
    IntegratedGraphics,
    Processor
}

// Picks the fastest device from `nemo-speech --json doctor`. The engine's own automatic choice
// takes the first Vulkan device, which on laptops is usually the slower integrated graphics.
public static class EngineDevices
{
    // The 0.6B Q8_0 models need about 0.7 GiB; the rest is room for a long dictation.
    internal const long MinimumFreeBytes = 1L << 30;

    public static EngineDevice Choose(string doctorJson, string preference)
    {
        if (preference == ModelDevices.Processor)
            return EngineDevice.Processor;

        var candidates = Read(doctorJson).Where(device => device.FreeBytes >= MinimumFreeBytes).Select(device => device.Device).ToList();
        return candidates.FirstOrDefault(device => device.Kind == EngineDeviceKind.GraphicsCard)
            ?? candidates.FirstOrDefault(device => device.Kind == EngineDeviceKind.IntegratedGraphics)
            ?? EngineDevice.Processor;
    }

    private static IEnumerable<(EngineDevice Device, long FreeBytes)> Read(string doctorJson)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(doctorJson);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("devices", out var devices) || devices.ValueKind != JsonValueKind.Array)
                yield break;

            foreach (var device in devices.EnumerateArray())
            {
                var kind = device.TryGetProperty("type", out var type) ? type.GetString() switch
                {
                    "gpu" => EngineDeviceKind.GraphicsCard,
                    "integrated-gpu" => EngineDeviceKind.IntegratedGraphics,
                    _ => (EngineDeviceKind?)null
                } : null;
                // Vulkan devices are named Vulkan0, Vulkan1, ... and selected as vulkan:0, vulkan:1, ...
                var name = device.TryGetProperty("name", out var value) ? value.GetString() ?? string.Empty : string.Empty;
                if (kind is null || !name.StartsWith("Vulkan", StringComparison.Ordinal) || !int.TryParse(name["Vulkan".Length..], out var index))
                    continue;

                var description = device.TryGetProperty("description", out var text) ? text.GetString() ?? name : name;
                var free = device.TryGetProperty("memory_free", out var memory) && memory.TryGetInt64(out var bytes) ? bytes : 0;
                yield return (new EngineDevice($"vulkan:{index}", description, kind.Value), free);
            }
        }
    }
}
