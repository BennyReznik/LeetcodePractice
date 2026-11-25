public IList<IList<string>> GroupAnagrams(string[] strs) { return strs.GroupBy(s=>string.Concat(Enumerable.Range('a',26).Select(c=>s.Count(x=>x==c)))).Select(g=>(IList<string>)g.ToList()).ToList(); }
