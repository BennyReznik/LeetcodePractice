public double FindMaxAverage(int[] nums, int k)
{
    int sum = 0;
    for (int i = 0; i < k; i++) sum += nums[i];
    int best = sum;
    for (int right = k; right < nums.Length; right++)
    {
        sum += nums[right] - nums[right - k];
        best = Math.Max(best, sum);
    }
    return (double)best / k;
}
