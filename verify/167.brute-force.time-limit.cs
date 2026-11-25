public int[] TwoSum(int[] a, int t) { for(int i=0;i<a.Length;i++) for(int j=i+1;j<a.Length;j++) if(a[i]+a[j]==t) return new[]{i+1,j+1}; return null; }
