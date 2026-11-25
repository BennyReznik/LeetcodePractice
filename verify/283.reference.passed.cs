public void MoveZeroes(int[] n) { int w=0; for(int r=0;r<n.Length;r++) if(n[r]!=0){ (n[w],n[r])=(n[r],n[w]); w++; } }
