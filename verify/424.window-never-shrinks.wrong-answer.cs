public int CharacterReplacement(string s, int k)
{
    var count = new int[26];
    int most = 0, best = 0;
    for (int right = 0; right < s.Length; right++)
    {
        most = Math.Max(most, ++count[s[right] - 'A']);
        best = Math.Max(best, Math.Min(right + 1, most + k));
    }
    return best;
}
