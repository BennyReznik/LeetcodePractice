public int MaxProfit(int[] prices)
{
    int best = 0;
    for (int i = 0; i < prices.Length; i++)
        for (int j = i + 1; j < prices.Length; j++)
            best = Math.Max(best, prices[j] - prices[i]);
    return best;
}
