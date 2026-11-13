using System.Windows;
using System.Windows.Media.Animation;

namespace PrivateType.App;

// The one motion for work under way: a slow, steady breath in opacity, 1.2 s out and back. It never runs while Windows has
// animations turned off; the words beside it ("Loading local model…", "Checking the files…") say the same thing, still.
internal static class CalmMotion
{
    private static readonly Duration HalfBreath = new(TimeSpan.FromSeconds(0.6));

    internal static bool Allowed => SystemParameters.ClientAreaAnimation;

    // Holds the element at its full opacity and, when motion is allowed, breathes it down to the lower one and back.
    internal static void Breathe(UIElement element, double full, double low)
    {
        Still(element, full);
        if (Allowed)
            element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(full, low, HalfBreath) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }

    internal static void Still(UIElement element, double opacity)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        element.Opacity = opacity;
    }
}
