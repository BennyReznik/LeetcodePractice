public int CharacterReplacement(string s, int k)
{
    int best = 0;
    for (int i = 0; i < s.Length; i++)
    {
        var count = new int[26];
        int most = 0;
        for (int j = i; j < s.Length; j++)
        {
            most = Math.Max(most, ++count[s[j] - 'A']);
            if (j - i + 1 - most > k) break;
            best = Math.Max(best, j - i + 1);
        }
    }
    return best;
}
