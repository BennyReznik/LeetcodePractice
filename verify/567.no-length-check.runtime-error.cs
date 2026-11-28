public bool CheckInclusion(string s1, string s2)
{
    var need = new int[26];
    var window = new int[26];
    foreach (char c in s1) need[c - 'a']++;
    for (int i = 0; i < s1.Length; i++) window[s2[i] - 'a']++;
    for (int right = s1.Length; ; right++)
    {
        if (need.AsSpan().SequenceEqual(window)) return true;
        if (right == s2.Length) return false;
        window[s2[right] - 'a']++;
        window[s2[right - s1.Length] - 'a']--;
    }
}
