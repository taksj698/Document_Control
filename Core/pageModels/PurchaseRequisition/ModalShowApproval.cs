﻿namespace Document_Control.Core.pageModels.PurchaseRequisition
{
	public class ModalShowApproval
	{
		public List<ApprovalDetail>? approvalDetails { get; set; }
	}
	public class ApprovalDetail
	{
		public string? Name { get; set; }
		public string? TelNo { get; set; }
	}
}