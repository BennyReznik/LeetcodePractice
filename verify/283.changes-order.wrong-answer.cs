public void MoveZeroes(int[] n) { int l=0,r=n.Length-1; while(l<r){ if(n[l]!=0) l++; else if(n[r]==0) r--; else (n[l],n[r])=(n[r],n[l]); } }
