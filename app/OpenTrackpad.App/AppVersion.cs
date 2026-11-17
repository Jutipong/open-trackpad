using System.Reflection;

namespace OpenTrackpad.App;

public static class AppVersion
{
    public static string Display { get; } = Read();

    private static string Read()
    {
        Assembly assembly = typeof(AppVersion).Assembly;

        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        Version? version = assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
