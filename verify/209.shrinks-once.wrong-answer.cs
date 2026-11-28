public int MinSubArrayLen(int target, int[] nums)
{
    int left = 0, sum = 0, shortest = int.MaxValue;
    for (int right = 0; right < nums.Length; right++)
    {
        sum += nums[right];
        if (sum >= target) { shortest = Math.Min(shortest, right - left + 1); sum -= nums[left++]; }
    }
    return shortest == int.MaxValue ? 0 : shortest;
}
