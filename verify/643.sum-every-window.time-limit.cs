public double FindMaxAverage(int[] nums, int k)
{
    double best = double.MinValue;
    for (int i = 0; i + k <= nums.Length; i++)
    {
        int sum = 0;
        for (int j = i; j < i + k; j++) sum += nums[j];
        best = Math.Max(best, (double)sum / k);
    }
    return best;
}
