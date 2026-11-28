public bool CheckInclusion(string s1, string s2)
{
    if (s1.Length > s2.Length) return false;
    var need = new int[26];
    var window = new int[26];
    foreach (char c in s1) need[c - 'a']++;
    for (int right = 0; right < s2.Length; right++)
    {
        window[s2[right] - 'a']++;
        if (right >= s1.Length) window[s2[right - s1.Length] - 'a']--;
        if (right >= s1.Length - 1 && need.AsSpan().SequenceEqual(window)) return true;
    }
    return false;
}
