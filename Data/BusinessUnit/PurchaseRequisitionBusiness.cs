﻿using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using AutoMapper;
using Document_Control.Configs.Extensions;
using Document_Control.Core.comModels;
using Document_Control.Core.dbModels;
using Document_Control.Core.pageModels.PurchaseRequisition;
using Document_Control.Data.Repository;
using Document_Control.Data.Repository.SQLServer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using WebGrease.Activities;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Document_Control.Data.BusinessUnit
{
	public class PurchaseRequisitionBusiness
	{
		private readonly WrapperRepository _wrapper;
		private SqlServerDbContext _dbContext;
		private IHttpContextAccessor _haccess;

		private List<Claim>? UserProfile;
		private int userId;
		private string? name;
		private int positionId;
		private string? position;
		public PurchaseRequisitionBusiness(IHttpContextAccessor haccess, WrapperRepository wrapper)
		{
			_wrapper = wrapper;
			_dbContext = _wrapper._dbContext;
			_haccess = haccess;

			var identity = (ClaimsIdentity)haccess.HttpContext.User.Identity;
			UserProfile = identity.Claims.ToList();
			var fineName = UserProfile.FirstOrDefault(x => x.Type == ClaimTypes.Name);
			if (fineName != null)
			{
				name = fineName.Value;
			}
			var finePosition = UserProfile.FirstOrDefault(x => x.Type == "PositionName");
			if (finePosition != null)
			{
				position = finePosition.Value;
			}
			var fineNameId = UserProfile.FirstOrDefault(x => x.Type == ClaimTypes.Sid);
			if (fineNameId != null)
			{
				userId = Convert.ToInt32(fineNameId.Value);
			}
			var finePositionId = UserProfile.FirstOrDefault(x => x.Type == "PositionId");
			if (finePositionId != null)
			{
				positionId = Convert.ToInt32(finePositionId.Value);
			}
		}

		public dynamic AddorUpdate(PagePR obj, string action)
		{
			int DocId = 0;
			int StatusId = 0;

			if (action == "บันทึกร่าง")
			{
				StatusId = 2;
			}
			else if (action == "บันทึก")
			{
				StatusId = 4;
			}
			else if (action == "ส่งกลับ")
			{
				StatusId = 3;
			}
			else if (action == "ยกเลิก")
			{
				StatusId = 6;
			}
			else if (action == "อนุมัติ")
			{
				StatusId = 4;
			}

			if (action == "บันทึก")
			{
				var lineApprove = GetLineApprove(null, obj.Budget);
				if (lineApprove == null || (lineApprove != null && lineApprove?.approvalLists?.Count() == 0))
				{
					return new { result = true, type = "error", message = "ไม่พบผู้อนุมัติ" };
				}
			}
			if (obj.Id != null && obj.Id != 0)
			{
				var find = _dbContext.TbDocumentTransaction.FirstOrDefault(x => x.Id == obj.Id);
				if (find != null)
				{
					//Update All only 
					List<int> StatusActionForCreator = new List<int>() { 1, 2, 3, 4 };
					if (StatusActionForCreator.Contains(find.StatusId))
					{
						var config = new MapperConfiguration(cfg => cfg.CreateMap<PagePR, TbDocumentTransaction>());
						var mapper = new Mapper(config);
						mapper.Map(obj, find);
						find.StatusId = StatusId;
						_dbContext.TbDocumentTransaction.Update(find);
						AddOrUpdateFile(find.Id, find.DocumentCode);
						_dbContext.SaveChanges();
					}
					DocId = find.Id;
				}
			}
			else
			{
				var date = DateTime.Now;
				TbDocumentTransaction data = new TbDocumentTransaction();
				var config = new MapperConfiguration(cfg => cfg.CreateMap<PagePR, TbDocumentTransaction>());
				var mapper = new Mapper(config);
				mapper.Map(obj, data);

				data.DocumentCode = _wrapper._storedProcedureRepository.GenarateCode();
				data.CreateBy = userId;
				data.CreateDate = date;
				data.OrderDate = date;
				data.StatusId = StatusId;
				_dbContext.TbDocumentTransaction.Add(data);
				_dbContext.SaveChanges();
				AddOrUpdateFile(data.Id, data.DocumentCode);
				DocId = data.Id;


			}
			StampApproval(DocId, obj.Budget.Value, action);
			StampHistory(DocId, action, obj.Reason, 0);

			//flow
			//file
			//approval
			//history

			return new { result = true, type = "success", message = "บันทึกรายการสำเร็จ", url = "Home/MyTask" };
		}
		public void AddOrUpdateFile(int DocId, string DocumentCode)
		{
			var sessionFile = _haccess.HttpContext.Session.GetString("docfile");
			var reqFile = string.IsNullOrEmpty(sessionFile)
				? new List<DocUpload>()
				: JsonConvert.DeserializeObject<List<DocUpload>>(sessionFile);

			var config = _dbContext.TbConfigs.FirstOrDefault(x => x.Name == "PathFile");
			var PathConfig = (config != null) ? config.Value : string.Empty;
			var part = Path.Combine(string.Format(@"{0}\{1}", PathConfig, DocumentCode));
			//remove
			var find = _dbContext.TbDocumentFile.Where(x => x.DocId == DocId).ToList();
			if (find != null)
			{
				foreach (var item in find)
				{
					var del = Path.Combine(string.Format(@"{0}\{1}", PathConfig, item.FileParth));

					if (File.Exists(Path.Combine(del)))
					{
						File.Delete(Path.Combine(del));
					}
					_dbContext.TbDocumentFile.Remove(item);
					_dbContext.SaveChanges();
				}
			}
			//create
			if (reqFile != null && reqFile.Count > 0)
			{
				bool exists = Directory.Exists(part);
				if (!exists)
					Directory.CreateDirectory(part);
				foreach (var item in reqFile)
				{
					using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(item.base64)))
					{
						Path.Combine(string.Format(@"{0}\{1}", part, item.filename));
					}
					byte[] fileBytes = Convert.FromBase64String(item.base64);
					string fullPath = Path.Combine(part, item.filename);
					Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
					File.WriteAllBytes(fullPath, fileBytes);

					var subpart = fullPath.Replace(PathConfig, string.Empty);
					_dbContext.TbDocumentFile.Add(new TbDocumentFile()
					{
						Extension = item.extension,
						CreateBy = userId,
						CreateDate = DateTime.Now,
						ContentType = item.ContentType,
						DocId = DocId,
						FileName = item.filename,
						FileParth = subpart
					});
					_dbContext.SaveChanges();
				}
			}
		}

		public void StampApproval(int DocId, decimal? Budget, string action)
		{
			List<string> status = new List<string>() { "บันทึก", "บันทึกร่าง", "ส่งกลับ" };
			if (status.Contains(action))
			{
				var lineApprove = GetLineApprove(null, Budget);
				var find = _dbContext.TbApprovalTransaction.Where(x => x.DocId == DocId).ToList();
				if (find != null && find.Count > 0)
				{
					_dbContext.TbApprovalTransaction.RemoveRange(find);
					_dbContext.SaveChanges();
				}
				if (lineApprove != null && lineApprove.approvalLists != null && lineApprove.approvalLists.Count > 0 && action != "ส่งกลับ")
				{
					List<TbApprovalTransaction> list = new List<TbApprovalTransaction>();

					foreach (var item in lineApprove.approvalLists)
					{
						list.Add(new TbApprovalTransaction()
						{
							DocId = DocId,
							Budget = Convert.ToDecimal(item.Budget),
							PositionId = item.PositionId.Value,
							IsApprove = false,
						});
					}
					if (list != null && list.Count > 0)
					{
						_dbContext.TbApprovalTransaction.AddRange(list);
						_dbContext.SaveChanges();
					}
				}
			}
			else
			{
				var FindNowApprover = _dbContext.TbApprovalTransaction.Where(x => x.DocId == DocId && !x.IsApprove).OrderBy(o => o.Budget).ToList();
				if (FindNowApprover != null && FindNowApprover.Count > 0)
				{
					var NowApprover = FindNowApprover.FirstOrDefault();
					if (NowApprover.PositionId == positionId)
					{
						NowApprover.IsApprove = true;
						NowApprover.ApproveBy = userId;
						_dbContext.TbApprovalTransaction.Update(NowApprover);
						_dbContext.SaveChanges();
					}
				}


				var find = _dbContext.TbDocumentTransaction.FirstOrDefault(x => x.Id == DocId);
				if (find != null)
				{
					//Update All only 
					List<int> StatusActionForApprover = new List<int>() { 4 };
					if (StatusActionForApprover.Contains(find.StatusId))
					{
						int StatusId = 0;
						var checklastapp = _dbContext.TbApprovalTransaction.Where(x => x.DocId == DocId && !x.IsApprove).ToList();
						if (checklastapp.Count == 0)
						{
							StatusId = 5;
						}
						else
						{
							StatusId = 4;
						}
						//update status only
						find.StatusId = StatusId;
						_dbContext.TbDocumentTransaction.Update(find);
						_dbContext.SaveChanges();
					}
				}
			}
		}
		public void StampHistory(int DocId, string action, string Reason, int StatusId)
		{
			_dbContext.TbHistoryTransaction.Add(new TbHistoryTransaction()
			{
				DocId = DocId,
				UserId = userId,
				PositionId = positionId,
				StatusId = StatusId,
				Action = action,
				Reason = Reason,
				StampDate = DateTime.Now
			});
			_dbContext.SaveChanges();
		}

		public async Task<dynamic> UploadDoc(IFormFile file)
		{
			if (file == null || file.Length == 0)
			{
				return new { result = true, type = "error", message = "ไม่พบไฟล์" };
			}
			try
			{
				var sessionFile = _haccess.HttpContext.Session.GetString("docfile");
				var reqFile = string.IsNullOrEmpty(sessionFile)
					? new List<DocUpload>()
					: JsonConvert.DeserializeObject<List<DocUpload>>(sessionFile);

				if (reqFile.Any(x => x.filename.Equals(file.FileName)))
				{
					return new { result = true, type = "error", message = "ไฟล์ซ้ำกัน" };
				}

				byte[] fileBytes;
				using (var stream = new MemoryStream())
				{
					await file.CopyToAsync(stream);
					fileBytes = stream.ToArray();
				}
				reqFile.Add(new DocUpload
				{
					id = Guid.NewGuid().ToString(),
					filename = file.FileName,
					extension = Path.GetExtension(file.FileName),
					base64 = Convert.ToBase64String(fileBytes),
					ContentType = file.ContentType
				});

				_haccess.HttpContext.Session.SetString("docfile", JsonConvert.SerializeObject(reqFile));

				return new { result = true, type = "success", message = "อัพโหลดรายการสำเร็จ" };
			}
			catch (Exception ex)
			{
				return new { result = true, type = "error", message = ex.Message };
			}
		}


		public dynamic deletefile(string id)
		{
			try
			{
				var sessionFile = _haccess.HttpContext.Session.GetString("docfile");
				var reqFile = string.IsNullOrEmpty(sessionFile)
					? new List<DocUpload>()
					: JsonConvert.DeserializeObject<List<DocUpload>>(sessionFile);

				reqFile = reqFile.Where(x => x.id != id).ToList();
				_haccess.HttpContext.Session.SetString("docfile", JsonConvert.SerializeObject(reqFile));
				return new { result = true, type = "success", message = "ลบรายการสำเร็จ" };
			}
			catch (Exception ex)
			{
				return new
				{
					result = true,
					type = "error",
					message = ex.Message
				};
			}
		}
		public List<DocUpload> GetDocFile(int? id)
		{
			var sessionFile = _haccess.HttpContext.Session.GetString("docfile");
			var reqFile = string.IsNullOrEmpty(sessionFile)
				? new List<DocUpload>()
				: JsonConvert.DeserializeObject<List<DocUpload>>(sessionFile);
			return reqFile;
		}




		public ApprovalPR GetLineApprove(int? id, decimal? budget)
		{
			ApprovalPR obj = new ApprovalPR();
			obj.approvalLists = new List<ApprovalList>();
			if (id != null && id != 0)
			{
				var find = (from ap in _dbContext.TbApprovalTransaction
							join po in _dbContext.TbPosition on ap.PositionId equals po.Id
							where ap.DocId == id
							select new
							{
								Budget = ap.Budget,
								PositionId = ap.PositionId,
								PositionName = po.PositionName,
								IsApprove = ap.IsApprove,
							})
							.ToList();
				if (find != null && find.Count > 0)
				{
					foreach (var item in find)
					{
						obj.approvalLists.Add(new ApprovalList()
						{
							Budget = item.Budget.ToString("N2"),
							PositionId = item.PositionId,
							PositionName = item.PositionName,
							IsApproved = item.IsApprove
						});
					}
				}
			}
			else if (budget != null && budget != 0)
			{
				var findApp = _dbContext.TbApprovalMatrix.Where(x => x.IsActive && x.Budget <= budget).ToList();
				var findPo = _dbContext.TbPosition.ToList();
				if (findApp != null)
				{
					foreach (var item in findApp)
					{
						var position = findPo.FirstOrDefault(x => x.Id == item.PositionId);
						if (position != null)
						{
							obj.approvalLists.Add(new ApprovalList()
							{
								Budget = item.Budget.ToString("N2"),
								PositionId = position.Id,
								PositionName = position.PositionName
							});
						}
					}
				}
			}
			return obj;
		}
		public ModalShowApproval GetPositionApproval(int? id)
		{
			ModalShowApproval obj = new ModalShowApproval();

			if (id != null)
			{
				obj.approvalDetails = _dbContext.TbUser.Where(x => x.PositionId == id && x.IsApprove)
					.Select(s => new ApprovalDetail() { Name = s.Name, TelNo = s.TelNo })
					.ToList();
			}
			return obj;
		}
		public PagePR GetData(int? Id)
		{
			PagePR obj = new PagePR();
			obj.lPriority = _dbContext.TbPriority
			.OrderBy(o => o.Seq)
			.Select(s => new SelectListItem()
			{
				Text = s.PriorityName,
				Value = s.Id.ToString()
			}).ToList();
			obj.lCompany = _dbContext.TbCompany
			.Select(s => new SelectListItem()
			{
				Text = s.CompanyName,
				Value = s.Id.ToString()
			}).ToList();
			obj.lDivision = _dbContext.TbDivision
			.Select(s => new SelectListItem()
			{
				Text = s.DivisionName,
				Value = s.Id.ToString()
			}).ToList();
			obj.lUser = _dbContext.TbUser
			.Select(s => new SelectListItem()
			{
				Text = s.Name,
				Value = s.Id.ToString()
			}).ToList();
			if (Id == null)
			{
				obj.OrderDate = DateTime.Now;
				obj.DocumentCode = "Auto Generate";
				obj.CreateBy = userId;
				obj.PositionId = positionId;
				//
				obj.CreateName = name;
				obj.PositionName = position;
			}
			else
			{
				var find = _dbContext.TbDocumentTransaction.FirstOrDefault(x => x.Id == Id);
				if (find != null)
				{
					var config = new MapperConfiguration(cfg => cfg.CreateMap<TbDocumentTransaction, PagePR>());
					var mapper = new Mapper(config);
					mapper.Map(find, obj);

					var user = _dbContext.TbUser.FirstOrDefault(x => x.Id == find.CreateBy);
					var position = _dbContext.TbPosition.FirstOrDefault(x => x.Id == positionId);
					obj.CreateName = user?.Name;
					obj.PositionName = position?.PositionName;
					// ถ้าเป็น New,Draft,Reject จะต้อง Fetch Line Approve ใหม่เสมอ ยกเว้น Flow นั้น บันทึกไปแล้วเพิ่มรออนุมัติ
					List<int> StatusActionForCreator = new List<int>() { 1, 2, 3 };
					if (StatusActionForCreator.Contains(find.StatusId))
					{
						obj.ApprovalPR = GetLineApprove(null, find.Budget);
					}
					else
					{
						obj.ApprovalPR = GetLineApprove(find.Id, null);
					}
					_haccess.HttpContext.Session.Remove("docfile");

					//GetDocFile
					var fildata = GetDocFile(Id);
					var file = _dbContext.TbDocumentFile.Where(x => x.DocId == Id).OrderBy(o => o.CreateDate).ToList();
					if (file != null)
					{
						var configp = _dbContext.TbConfigs.FirstOrDefault(x => x.Name == "PathFile");
						var PathConfig = (configp != null) ? configp.Value : string.Empty;
						foreach (var item in file)
						{
							var fullpart = Path.Combine(string.Format(@"{0}\{1}", PathConfig, item.FileParth));
							MemoryStream destination = new MemoryStream();
							using (FileStream source = File.Open(fullpart, FileMode.Open))
							{
								source.CopyTo(destination);
								fildata.Add(new DocUpload()
								{
									base64 = Convert.ToBase64String(destination.ToArray()),
									ContentType = item.ContentType,
									filename = item.FileName,
									extension = item.Extension,
									id = Guid.NewGuid().ToString(),
								});
							}
						}
						_haccess.HttpContext.Session.SetString("docfile", JsonConvert.SerializeObject(fildata));
					}
					obj.DocUpload = fildata;
				}
			}
			return obj;
		}
	}
}

