public int LengthOfLongestSubstring(string s)
{
    var window = new HashSet<char>();
    int left = 0, best = 0;
    for (int right = 0; right < s.Length; right++)
    {
        while (window.Contains(s[right])) window.Remove(s[left++]);
        window.Add(s[right]);
        best = Math.Max(best, right - left + 1);
    }
    return best;
}
