namespace Harbor.Core;

/// <summary>
/// 출석(연속 접속) 보상. 하루에 한 번만 받고, 연속으로 올수록 늘어나되 상한이 있다.
/// 상한이 없으면 오래 한 사람만 부자가 되고, 하루 빠지면 손해가 너무 커서 오히려 부담이 된다.
/// </summary>
public static class Attendance
{
    /// <summary>보너스가 늘어나는 최대 일수. 이후로는 계속 최대치.</summary>
    public const int MaxBonusDays = 7;

    /// <summary>
    /// 오늘 기준 새 연속 일수.
    /// 처음이면 1, 어제 왔으면 +1, 오늘 이미 받았으면 그대로, 하루라도 건너뛰었으면 1 부터 다시.
    /// </summary>
    public static int NextStreak(DateOnly? last, DateOnly today, int streak)
    {
        if (last is null) return 1;
        if (last == today) return Math.Max(1, streak);        // 이미 받은 날 — 그대로 둔다
        if (last == today.AddDays(-1)) return streak + 1;     // 연속
        return 1;                                             // 끊김
    }

    /// <summary>연속 n일째 보상. 1일차 = base, 하루 늘 때마다 step 씩, MaxBonusDays 에서 멈춘다.</summary>
    public static long Reward(long baseAmount, long stepAmount, int streak)
        => baseAmount + stepAmount * Math.Clamp(streak - 1, 0, MaxBonusDays - 1);

    /// <summary>오늘이 며칠째인지 사람이 읽을 문구.</summary>
    public static string Describe(int streak)
        => streak >= MaxBonusDays ? $"{streak}일 연속 (최대 보너스)" : $"{streak}일 연속";
}
