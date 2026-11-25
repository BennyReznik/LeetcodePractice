public int[] TwoSum(int[] nums, int t) { for (int i=0;i<nums.Length;i++) for (int j=i+1;j<nums.Length;j++) if (nums[i]+nums[j]==t) return new[]{i,j}; return null; }
