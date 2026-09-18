namespace Harbor.Core;

/// <summary>
/// 수확물 판매 계산. 낱개보다 묶음이 이득이라 "모아서 한 번에 파는" 재미가 생긴다.
/// 순수 함수라 서버와 테스트가 같은 값을 본다.
/// </summary>
public static class Trade
{
    /// <param name="Qty">판 개수</param>
    /// <param name="Bundles">그중 묶음으로 처리된 횟수</param>
    /// <param name="Total">받을 루피</param>
    public readonly record struct Quote(int Qty, int Bundles, long Total);

    /// <summary>
    /// 묶음을 먼저 최대한 채우고 나머지는 낱개로 친다.
    /// bundleQty 나 bundlePrice 가 0 이면 묶음 없이 전부 낱개.
    /// </summary>
    public static Quote QuoteSell(int qty, long unitPrice, int bundleQty, long bundlePrice)
    {
        if (qty <= 0 || unitPrice < 0) return new Quote(0, 0, 0);

        int bundles = bundleQty > 0 && bundlePrice > 0 ? qty / bundleQty : 0;
        int singles = qty - bundles * bundleQty;
        return new Quote(qty, bundles, bundles * bundlePrice + singles * unitPrice);
    }

    /// <summary>묶음이 낱개보다 이득인가. 정의 파일이 잘못 적혔는지 확인하는 용도.</summary>
    public static bool BundleIsWorthIt(long unitPrice, int bundleQty, long bundlePrice)
        => bundleQty > 0 && bundlePrice > unitPrice * bundleQty;
}
