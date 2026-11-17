using System.Globalization;
using System.Windows;

namespace OpenTrackpad.App;

public static class Loc
{
    private static ResourceDictionary? _strings;
    private static string _language = "en";

    public static string Language => _language;

    public static event Action? LanguageChanged;

    public static void Initialize(string? language) => SetLanguage(language, notify: false);

    public static void SetLanguage(string? language) => SetLanguage(language, notify: true);

    private static void SetLanguage(string? language, bool notify)
    {
        string normalized = string.Equals(language, "th", StringComparison.OrdinalIgnoreCase)
            ? "th"
            : "en";

        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{normalized}.xaml", UriKind.Relative),
        };

        Application? app = Application.Current;
        if (app is null)
        {
            _strings = dictionary;
            _language = normalized;
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        if (_strings is not null)
            merged.Remove(_strings);
        merged.Add(dictionary);
        _strings = dictionary;

        bool changed = _language != normalized;
        _language = normalized;
        if (changed && notify)
            LanguageChanged?.Invoke();
    }

    public static string T(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}
