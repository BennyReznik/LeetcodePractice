public bool IsAnagram(string s, string t) { if (s.Length!=t.Length) return false; var h=new HashSet<char>(s); foreach(char x in t) if(!h.Contains(x)) return false; return true; }
