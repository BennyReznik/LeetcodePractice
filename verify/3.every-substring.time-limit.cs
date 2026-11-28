public int LengthOfLongestSubstring(string s)
{
    int best = 0;
    for (int i = 0; i < s.Length; i++)
        for (int j = i + 1; j <= s.Length; j++)
            if (s.Substring(i, j - i).Distinct().Count() == j - i) best = Math.Max(best, j - i);
    return best;
}
