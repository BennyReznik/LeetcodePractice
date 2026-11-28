public double FindMaxAverage(int[] nums, int k)
{
    int sum = 0, best = 0;
    for (int i = 0; i < nums.Length; i++)
    {
        sum += nums[i];
        if (i >= k) sum -= nums[i - k];
        if (i >= k - 1) best = Math.Max(best, sum);
    }
    return (double)best / k;
}
