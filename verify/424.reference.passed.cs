public int CharacterReplacement(string s, int k)
{
    var count = new int[26];
    int left = 0, most = 0, best = 0;
    for (int right = 0; right < s.Length; right++)
    {
        most = Math.Max(most, ++count[s[right] - 'A']);
        while (right - left + 1 - most > k) count[s[left++] - 'A']--;
        best = Math.Max(best, right - left + 1);
    }
    return best;
}
