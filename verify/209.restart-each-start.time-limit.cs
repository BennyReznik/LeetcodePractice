public int MinSubArrayLen(int target, int[] nums)
{
    int shortest = int.MaxValue;
    for (int i = 0; i < nums.Length; i++)
    {
        int sum = 0;
        for (int j = i; j < nums.Length; j++)
        {
            sum += nums[j];
            if (sum >= target) { shortest = Math.Min(shortest, j - i + 1); break; }
        }
    }
    return shortest == int.MaxValue ? 0 : shortest;
}
