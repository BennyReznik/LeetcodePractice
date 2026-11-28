public bool CheckInclusion(string s1, string s2)
{
    var sorted = new string(s1.Order().ToArray());
    for (int i = 0; i + s1.Length <= s2.Length; i++)
        if (new string(s2.Substring(i, s1.Length).Order().ToArray()) == sorted) return true;
    return false;
}
