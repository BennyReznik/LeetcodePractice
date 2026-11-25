public int[] TwoSum(int[] a, int t) { int l=0,r=a.Length-1; while(l<r){ if(a[l]+a[r]==t) return new[]{l+1,r+1}; } return null; }
