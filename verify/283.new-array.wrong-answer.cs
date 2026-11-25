public void MoveZeroes(int[] nums) { nums = nums.OrderBy(x=>x==0).ToArray(); }
