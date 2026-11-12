namespace PrivateType.App;

internal static class ApplicationVersion
{
    internal static Version? Current => typeof(ApplicationVersion).Assembly.GetName().Version;

    internal static string Label(Version? version) =>
        version is null ? "PrivateType (version unknown)" : $"PrivateType {Number(version)}";

    // The version alone, such as "1.0.4", as Settings shows it beside the product name.
    internal static string Number(Version? version)
    {
        if (version is null)
            return "version unknown";

        var fieldCount = version.Revision > 0 ? 4 : version.Build > 0 ? 3 : 2;
        return version.ToString(fieldCount);
    }
}
