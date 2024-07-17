﻿namespace Document_Control.Core.pageModels.Home
{
	public class Worklist
	{
		public List<WorklistData>? data { get; set; }
	}
	public class WorklistData
	{
		public DateTime DocumentDate { get; set; }
		public string DocumentCode { get; set; }
		public int DocumentId { get; set; }
		public string Name { get; set; }
		public string Subject { get; set; }
		public string Priority { get; set; }
		public string Status { get; set; }
		public int ApproveByPositionId { get; set; }
		public string ApproveByPositionName { get; set; }
	}
}
