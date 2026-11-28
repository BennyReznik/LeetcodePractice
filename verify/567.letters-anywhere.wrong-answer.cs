public bool CheckInclusion(string s1, string s2) => s1.All(c => s2.Contains(c));
