public int LengthOfLongestSubstring(string s)
{
    var lastSeen = new Dictionary<char, int>();
    int left = 0, best = 0;
    for (int right = 0; right < s.Length; right++)
    {
        if (lastSeen.TryGetValue(s[right], out int seen)) left = seen + 1;
        lastSeen[s[right]] = right;
        best = Math.Max(best, right - left + 1);
    }
    return best;
}
