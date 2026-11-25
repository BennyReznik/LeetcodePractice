public int MaxArea(int[] h) { int b=0; for(int i=0;i<h.Length;i++) for(int j=i+1;j<h.Length;j++) b=Math.Max(b,(j-i)*Math.Min(h[i],h[j])); return b; }
