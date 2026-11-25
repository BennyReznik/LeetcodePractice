public IList<IList<string>> GroupAnagrams(string[] strs) { return strs.GroupBy(s=>s).Select(g=>(IList<string>)g.ToList()).ToList(); }
