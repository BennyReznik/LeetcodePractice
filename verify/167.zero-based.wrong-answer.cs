public int[] TwoSum(int[] a, int t) { int l=0,r=a.Length-1; while(l<r){ int s=a[l]+a[r]; if(s==t) return new[]{l,r}; if(s<t) l++; else r--; } return null; }
