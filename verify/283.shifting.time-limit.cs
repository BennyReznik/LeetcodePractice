public void MoveZeroes(int[] n) { int end=n.Length; for(int i=0;i<end;){ if(n[i]!=0){i++;continue;} for(int j=i;j<end-1;j++) n[j]=n[j+1]; n[--end]=0; } }
