using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Markup;

namespace costats.App.Localization;

/// <summary>
/// 화면 문구의 언어 전환. 영어 원문이 곧 키이고, 한국어는 LocStrings 의 사전·패턴에서 찾는다.
/// 계약: XAML 은 {loc:Tr '영어 원문'} 으로, 코드는 Loc.T(...) 로 읽는다 — 사전에 없으면 원문이 그대로 나간다.
/// 함정: 코어·인프라 층이 만든 문장(리셋 시각 등)은 영어로 온다 — Loc.Tr 가 패턴으로 옮기므로 원문 형식이 바뀌면 여기도 고쳐야 한다.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public const string Korean = "ko";
    public const string English = "en";

    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>언어가 바뀐 뒤 불린다 — 코드가 만든 문자열을 다시 그릴 곳이 구독한다.</summary>
    public static event Action? LanguageChanged;

    public static string Language { get; private set; } = Korean;

    public static bool IsKorean => Language == Korean;

    // 계약: 값 자체는 의미가 없고, 바뀌었다는 알림이 {loc:Tr} 바인딩을 전부 다시 평가시킨다
    public int Version { get; private set; }

    public static void SetLanguage(string? language)
    {
        var next = language == English ? English : Korean;
        if (next == Language && Instance.Version > 0)
        {
            return;
        }

        Language = next;
        Instance.Version++;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(nameof(Version)));
        LanguageChanged?.Invoke();
    }

    /// <summary>고정 문구를 옮긴다. 영어 모드에서는 키별 영어 문안(있으면) 또는 원문.</summary>
    public static string T(string text)
    {
        if (IsKorean)
        {
            return LocStrings.Korean.TryGetValue(text, out var ko) ? ko : text;
        }

        return LocStrings.EnglishOverrides.TryGetValue(text, out var en) ? en : text;
    }

    public static string T(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, T(format), args);

    /// <summary>다른 층이 만든 영어 문장을 옮긴다 — 사전에 없으면 패턴을 차례로 대 본다.</summary>
    public static string Tr(string? text)
    {
        if (string.IsNullOrEmpty(text) || !IsKorean)
        {
            return text ?? string.Empty;
        }

        if (LocStrings.Korean.TryGetValue(text, out var exact))
        {
            return exact;
        }

        foreach (var (pattern, replace) in LocStrings.KoreanPatterns)
        {
            var match = pattern.Match(text);
            if (match.Success)
            {
                return replace(match);
            }
        }

        return text;
    }

    /// <summary>"3d 17h" · "26m" 같은 기간 표기를 "3일 17시간" 으로 옮긴다.</summary>
    public static string Duration(string text)
    {
        return Regex.Replace(text, @"(\d+)\s*(d|h|m|s)\b", m => m.Groups[1].Value + m.Groups[2].Value switch
        {
            "d" => "일",
            "h" => "시간",
            "m" => "분",
            _ => "초"
        });
    }
}

/// <summary>
/// XAML 용: Text="{loc:Tr 'Refresh now'}". 언어가 바뀌면 다시 평가된다.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding(nameof(Loc.Version))
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay,
            Converter = KeyConverter.Instance,
            ConverterParameter = Key
        };
        return binding.ProvideValue(serviceProvider);
    }

    private sealed class KeyConverter : IValueConverter
    {
        public static readonly KeyConverter Instance = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Loc.T((string)parameter);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
    }
}
