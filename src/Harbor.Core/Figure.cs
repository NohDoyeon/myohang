namespace Harbor.Core;

/// <summary>
/// 아바타 외모 문자열. `파츠-모델-팔레트` 조각을 '.' 로 이어 붙인 것.
/// 예: `hd-001-02.hr-002-05.ch-001-03.lg-001-01.sh-001-01.ha-001-04`
/// 서버는 내용을 해석하지 않고 형식만 검사한다(렌더는 클라이언트 몫). 저장·전파되므로 길이와 문자를 제한한다.
/// </summary>
public static class Figure
{
    /// <summary>hd=머리(피부), hr=머리카락, ch=상의, lg=하의, sh=신발, ha=모자(없어도 됨).</summary>
    public static readonly string[] Parts = { "hd", "hr", "ch", "lg", "sh", "ha" };

    public const string Default = "hd-001-02.hr-001-01.ch-001-01.lg-001-01.sh-001-01";
    public const int MaxLength = 96;
    public const int MaxModel = 999;
    public const int MaxPalette = 99;

    public static bool IsValid(string? figure)
    {
        if (string.IsNullOrWhiteSpace(figure) || figure.Length > MaxLength) return false;
        var seen = new HashSet<string>();
        var segments = figure.Split('.');
        if (segments.Length == 0 || segments.Length > Parts.Length) return false;
        foreach (var seg in segments)
        {
            var p = seg.Split('-');
            if (p.Length != 3) return false;
            if (Array.IndexOf(Parts, p[0]) < 0) return false;
            if (!seen.Add(p[0])) return false;                       // 같은 파츠 중복 금지
            if (!IsNumber(p[1], MaxModel) || !IsNumber(p[2], MaxPalette)) return false;
        }
        return true;
    }

    private static bool IsNumber(string s, int max)
    {
        if (s.Length is 0 or > 3) return false;
        foreach (char c in s) if (c is < '0' or > '9') return false;
        int v = int.Parse(s);
        return v >= 1 && v <= max;
    }

    /// <summary>형식이 깨졌으면 기본 외모로 되돌린다.</summary>
    public static string Sanitize(string? figure) => IsValid(figure) ? figure! : Default;

    /// <summary>파츠의 (모델, 팔레트). 없으면 null.</summary>
    public static (int model, int palette)? Get(string? figure, string part)
    {
        if (string.IsNullOrEmpty(figure)) return null;
        foreach (var seg in figure.Split('.'))
        {
            var p = seg.Split('-');
            if (p.Length == 3 && p[0] == part && int.TryParse(p[1], out int m) && int.TryParse(p[2], out int pal))
                return (m, pal);
        }
        return null;
    }

    /// <summary>조각들을 Parts 순서로 정렬해 문자열로. 값이 null 인 파츠는 뺀다(모자 벗기).</summary>
    public static string Build(IReadOnlyDictionary<string, (int model, int palette)> parts)
    {
        var segs = new List<string>();
        foreach (var name in Parts)
            if (parts.TryGetValue(name, out var v))
                segs.Add($"{name}-{v.model:000}-{v.palette:00}");
        return segs.Count > 0 ? string.Join('.', segs) : Default;
    }
}
