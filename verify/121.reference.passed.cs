public int MaxProfit(int[] prices)
{
    int lowest = int.MaxValue, best = 0;
    foreach (int price in prices)
    {
        lowest = Math.Min(lowest, price);
        best = Math.Max(best, price - lowest);
    }
    return best;
}
