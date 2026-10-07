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

    private static string Describe(EngineDevice device) => device.Kind switch
    {
        EngineDeviceKind.GraphicsCard => $"{device.Description} graphics card",
        EngineDeviceKind.IntegratedGraphics => $"{device.Description} integrated graphics",
        _ => "processor"
    };
}
