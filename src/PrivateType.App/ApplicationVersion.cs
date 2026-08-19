namespace PrivateType.App;

internal static class ApplicationVersion
{
    internal static Version? Current => typeof(ApplicationVersion).Assembly.GetName().Version;

    internal static string Label(Version? version)
    {
        if (version is null)
            return "PrivateType (version unknown)";

        var fieldCount = version.Revision > 0 ? 4 : version.Build > 0 ? 3 : 2;
        return $"PrivateType {version.ToString(fieldCount)}";
    }
}
