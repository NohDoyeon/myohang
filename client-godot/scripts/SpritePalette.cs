using Godot;

namespace HarborClient;

/// <summary>
/// 고양이 도트에 실제로 쓰인 색 → 고른 색으로 바꾸는 표.
///
/// 색 목록은 `tools/atlas-palette.py` 로 그림에서 직접 뽑았고,
/// `art/ref/sprite-prompt.md` 의 팔레트 지정과 일치한다(그래서 새 그림도 같은 표로 처리된다).
///
/// **명암을 유지하는 것이 요점.** 고른 색 하나로 9가지 털색을 전부 덮으면 평평해지므로,
/// 원본의 밝기 비율과 채도 차이를 그대로 옮겨 단계를 만든다.
/// </summary>
public static class SpritePalette
{
    // 그룹을 나눠 둔 것이 요점이다. 바탕과 무늬를 한 덩어리로 묶으면 색을 하나밖에 못 고르고,
    // 그러면 "내 고양이를 닮게" 만들 수가 없다. 흰 바탕 + 검정 무늬, 크림 바탕 + 주황 무늬처럼
    // **조합**이 되어야 한다.

    /// <summary>털 바탕 — 밝은 크림 4색. figure `hd`.</summary>
    public static readonly Color[] Base =
    {
        new("fcf0e1"), new("fcf0e3"), new("fdf3e8"), new("f3e2d2"),
    };

    /// <summary>무늬 — 태비 줄무늬와 음영 5색. figure `hr`.</summary>
    public static readonly Color[] Markings =
    {
        new("dbbba4"), new("e6b9a3"), new("d8a786"), new("cd9d7d"), new("a47462"),
    };

    /// <summary>상의 — 밝은 면과 그늘. figure `ch`.</summary>
    public static readonly Color[] Shirt = { new("a0afce"), new("958c9a") };

    /// <summary>귀 안쪽 한 색. figure `ea`.</summary>
    public static readonly Color[] Ear = { new("e29f8b") };

    // 눈은 표에 없다. `5b4854` 를 바꿔 봤지만 **화면에서 아무 변화가 없었다**(2026-09-18 확인) —
    // 눈동자가 외곽선(503433)과 같은 색으로 찍혀 있어서, 그걸 바꾸면 선까지 같이 물들어 얼굴이 무너진다.
    // 눈 색을 따로 고르게 하려면 **눈동자를 다른 색으로 찍은 그림**이 필요하다.

    public static int PairCount => Base.Length + Markings.Length + Shirt.Length + Ear.Length;

    /// <summary>셰이더가 선언한 배열 길이. 남는 칸은 절대 일치하지 않는 값으로 채운다(`pair_count` 가 어차피 잘라 준다).</summary>
    private const int Slots = 16;
    private static readonly Vector3 Unused = new(-1, -1, -1);

    /// <summary>원본 색 목록 (셰이더 uniform 순서와 같다).</summary>
    public static Vector3[] Sources()
    {
        var v = Filled();
        int n = 0;
        foreach (var c in Base) v[n++] = Rgb(c);
        foreach (var c in Markings) v[n++] = Rgb(c);
        foreach (var c in Shirt) v[n++] = Rgb(c);
        foreach (var c in Ear) v[n++] = Rgb(c);
        return v;
    }

    /// <summary>고른 색들로 바뀐 목록. **순서는 Sources() 와 같아야 한다.**</summary>
    public static Vector3[] Targets(Color baseColor, Color markings, Color shirt, Color ear)
    {
        var v = Filled();
        int n = 0;
        foreach (var c in Base) v[n++] = Rgb(Ramp(c, Base, baseColor));
        foreach (var c in Markings) v[n++] = Rgb(Ramp(c, Markings, markings));
        foreach (var c in Shirt) v[n++] = Rgb(Ramp(c, Shirt, shirt));
        foreach (var _ in Ear) v[n++] = Rgb(ear);        // 한 색짜리는 사다리를 만들 것이 없다
        return v;
    }

    private static Vector3[] Filled()
    {
        var v = new Vector3[Slots];
        for (int i = 0; i < Slots; i++) v[i] = Unused;
        return v;
    }

    private static Vector3 Rgb(Color c) => new(c.R, c.G, c.B);

    /// <summary>
    /// 원본 색을 목표색의 **명암 사다리 위 한 칸**으로 옮긴다.
    ///
    /// 밝기를 비율로 옮기면(`target.V * src.V / base.V`) 어두운 색을 고를 때 9단계가 전부 바닥에 몰려
    /// 한 덩어리로 보인다. 그래서 원본 그룹 안에서의 **상대 위치(가장 어두운 색 0 ~ 가장 밝은 색 1)** 를 구하고,
    /// 목표색을 어둡게/밝게 한 양 끝 사이에서 그 위치를 찍는다 — 어떤 색을 골라도 단계가 남는다.
    /// </summary>
    private static Color Ramp(Color src, Color[] group, Color target)
    {
        float min = 1f, max = 0f;
        foreach (var c in group) { min = Mathf.Min(min, c.V); max = Mathf.Max(max, c.V); }
        float t = max - min < 0.001f ? 1f : (src.V - min) / (max - min);

        var dark = target.Lerp(new Color(0.08f, 0.06f, 0.09f), 0.45f);   // 그늘
        var light = target.Lerp(Colors.White, 0.30f);                    // 빛 받는 면
        var c2 = dark.Lerp(light, t);
        // 원본에서 채도가 높았던 칸(태비 무늬)은 목표색에서도 조금 더 진하게 남긴다.
        float s = Mathf.Clamp(c2.S + (src.S - group[0].S) * 0.5f, 0f, 1f);
        return Color.FromHsv(c2.H, s, c2.V);
    }
}
