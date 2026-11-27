using PrivateType.Core;

namespace PrivateType.App;

internal enum ModelDeviceState
{
    Running,
    FellBack,
    NotLoaded
}

// The status line under "Run model on" in Settings.
internal sealed record ModelDeviceStatus(ModelDeviceState State, string Text)
{
    public static ModelDeviceStatus Of(EngineDevice? running, bool fellBack, EngineDevice predicted)
        => running is null
            ? new(ModelDeviceState.NotLoaded, $"Not loaded — will use {Describe(predicted)}")
            : fellBack
                ? new(ModelDeviceState.FellBack, "Now running on: processor — the graphics card could not start")
                : new(ModelDeviceState.Running, $"Now running on: {Describe(running)}");

    // The in-process engine names its device only once loaded ("GPU: NVIDIA GeForce MX450 (D3D12)" or
    // "CPU (Avx2)"). Before that, the server's choice is the best guess, except that the in-process engine
    // skips integrated graphics and uses the processor instead.
    public static ModelDeviceStatus OfInProcess(string? backend, bool usingGpu, EngineDevice predicted)
        => backend is null
            ? new(ModelDeviceState.NotLoaded, $"Not loaded — will use {Describe(predicted.Kind == EngineDeviceKind.IntegratedGraphics ? EngineDevice.Processor : predicted)}")
            : new(ModelDeviceState.Running, $"Now running on: {(usingGpu ? $"{GpuName(backend)} graphics card" : "processor")}");

    private static string GpuName(string backend)
    {
        const string Prefix = "GPU: ", Suffix = " (D3D12)";
        var name = backend.StartsWith(Prefix, StringComparison.Ordinal) ? backend[Prefix.Length..] : backend;
        return name.EndsWith(Suffix, StringComparison.Ordinal) ? name[..^Suffix.Length] : name;
    }

    private static string Describe(EngineDevice device) => device.Kind switch
    {
        EngineDeviceKind.GraphicsCard => $"{device.Description} graphics card",
        EngineDeviceKind.IntegratedGraphics => $"{device.Description} integrated graphics",
        _ => "processor"
    };
}
