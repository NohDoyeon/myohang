namespace Harbor.Core;

/// <summary>
/// 복권 확률표. 순수 함수라 서버·테스트에서 같은 결과를 검증할 수 있다.
/// 기대값이 가격보다 낮아야 한다(돈이 빠져나가는 곳). 버는 곳은 일일 용돈·미니게임.
/// </summary>
public static class Lottery
{
    public readonly record struct Prize(int Rank, long Amount);

    /// <summary>확률 구간 — 1등 0.2%, 2등 1%, 3등 5%, 4등(본전) 20%, 나머지 꽝.</summary>
    public const double P1 = 0.002, P2 = 0.012, P3 = 0.062, P4 = 0.262;

    /// <param name="roll">[0,1) 난수.</param>
    /// <param name="jackpot">1등 상금. 2등 = 1/5, 3등 = 1/20.</param>
    /// <param name="price">한 장 값 — 4등은 본전.</param>
    public static Prize Draw(double roll, long jackpot, long price)
    {
        if (roll < P1) return new Prize(1, jackpot);
        if (roll < P2) return new Prize(2, jackpot / 5);
        if (roll < P3) return new Prize(3, jackpot / 20);
        if (roll < P4) return new Prize(4, price);
        return new Prize(0, 0);
    }

    /// <summary>한 장당 기대 상금. 가격보다 작아야 정상.</summary>
    public static double ExpectedValue(long jackpot, long price)
        => P1 * jackpot + (P2 - P1) * (jackpot / 5.0) + (P3 - P2) * (jackpot / 20.0) + (P4 - P3) * price;

    public static string RankName(int rank) => rank switch
    {
        1 => "1등", 2 => "2등", 3 => "3등", 4 => "4등", _ => "꽝",
    };
}
