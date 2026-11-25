public int MaxArea(int[] h) { int l=0,r=h.Length-1,b=0; while(l<r){ b=Math.Max(b,(r-l)*Math.Min(h[l],h[r])); if(h[l]<h[r]) l++; else r--; } return b; }
