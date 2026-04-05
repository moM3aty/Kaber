// مسار الملف: Controllers/HRController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,HR")]
    public class HRController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HRController(ApplicationDbContext context) { _context = context; }

        private void LogAction(string actionType, string details)
        {
            var username = User.Identity?.Name ?? "مستخدم غير معروف";
            var role = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "صلاحية غير محددة";

            _context.SystemLogs.Add(new SystemLog
            {
                ActionType = actionType,
                Details = details,
                Username = $"{username} - [{role}]",
                Timestamp = DateTime.Now
            });
        }

        public async Task<IActionResult> Index()
        {
            var employees = await _context.SystemUsers.OrderBy(u => u.Role).ToListAsync();
            ViewBag.TotalEmployees = employees.Count;
            ViewBag.ActiveLeaves = await _context.LeaveRequests.CountAsync(l => l.Status == LeaveStatus.Approved && !l.IsReturned);
            ViewBag.PendingLeaves = await _context.LeaveRequests.CountAsync(l => l.Status == LeaveStatus.Pending);

            return View(employees);
        }

        // ==========================================
        // 📌 1. الإجازات
        // ==========================================
        public async Task<IActionResult> Leaves()
        {
            var leaves = await _context.LeaveRequests
                .Include(l => l.User)
                .OrderByDescending(l => l.RequestDate)
                .ToListAsync();

            ViewData["UserId"] = new SelectList(await _context.SystemUsers.ToListAsync(), "UserId", "Username");
            return View(leaves);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestLeave(LeaveRequest leave)
        {
            if (ModelState.IsValid)
            {
                _context.Add(leave);
                LogAction("طلب إجازة", $"تم تقديم طلب إجازة للموظف (ID: {leave.UserId}) من {leave.StartDate:yyyy-MM-dd} إلى {leave.EndDate:yyyy-MM-dd}");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تقديم طلب الإجازة بنجاح.";
            }
            return RedirectToAction(nameof(Leaves));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateLeaveStatus(int id, LeaveStatus status, string note, decimal deductionAmount)
        {
            var leave = await _context.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == id);
            if (leave != null)
            {
                leave.Status = status;
                leave.AdminNote = note;

                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.Name == leave.User.Username);

                if (status == LeaveStatus.Approved)
                {
                    if (tech != null)
                    {
                        tech.IsAvailable = false;
                        _context.Update(tech);

                        if (deductionAmount > 0)
                        {
                            _context.Expenses.Add(new Expense
                            {
                                Description = $"خصم إجازة ({leave.Type}) للفترة من {leave.StartDate:MM/dd} إلى {leave.EndDate:MM/dd}",
                                Amount = deductionAmount,
                                DeductionFrom = DeductionSource.Technician,
                                TechnicianId = tech.TechnicianId,
                                Date = DateTime.Now,
                                RecordedBy = User.Identity?.Name ?? "نظام الـ HR"
                            });
                        }
                    }
                }

                LogAction("قرار إجازة", $"تم {status} إجازة الموظف {leave.User.Username}. قيمة الخصم: {deductionAmount}");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم اعتماد الإجازة بنجاح.";
            }
            return RedirectToAction(nameof(Leaves));
        }

        [HttpPost]
        public async Task<IActionResult> ReturnToWork(int leaveId)
        {
            var leave = await _context.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == leaveId);
            if (leave != null && leave.Status == LeaveStatus.Approved && !leave.IsReturned)
            {
                leave.IsReturned = true;
                leave.ActualReturnDate = DateTime.Now;
                _context.Update(leave);

                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.Name == leave.User.Username);
                if (tech != null)
                {
                    tech.IsAvailable = true;
                    _context.Update(tech);
                }

                LogAction("مباشرة عمل", $"تم تسجيل مباشرة عمل للموظف {leave.User.Username} وعودته للخدمة.");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تسجيل مباشرة العمل وعودة الموظف بنجاح.";
            }
            return RedirectToAction(nameof(Leaves));
        }

        // ==========================================
        // 📌 2. الحضور والغياب (Attendance)
        // ==========================================
        public async Task<IActionResult> Attendance(string date)
        {
            DateTime targetDate = string.IsNullOrEmpty(date) ? DateTime.Today : DateTime.Parse(date);
            ViewBag.CurrentDate = targetDate.ToString("yyyy-MM-dd");

            var users = await _context.SystemUsers.ToListAsync();
            var attendanceForDay = await _context.AttendanceRecords
                .Where(a => a.Date.Date == targetDate.Date)
                .ToListAsync();

            var records = new List<dynamic>();
            foreach (var u in users)
            {
                var att = attendanceForDay.FirstOrDefault(a => a.UserId == u.UserId);
                records.Add(new
                {
                    UserId = u.UserId,
                    Username = u.Username,
                    Role = u.Role,
                    RecordId = att?.Id ?? 0,
                    Status = att?.Status.ToString() ?? "None",
                    Note = att?.Note ?? ""
                });
            }

            ViewBag.AttendanceList = records;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SaveAttendance(int userId, DateTime date, string status, string note)
        {
            var existing = await _context.AttendanceRecords.FirstOrDefaultAsync(a => a.UserId == userId && a.Date.Date == date.Date);

            if (status == "None")
            {
                if (existing != null) _context.AttendanceRecords.Remove(existing);
            }
            else
            {
                AttendanceStatus parsedStatus = status == "Present" ? AttendanceStatus.Present : AttendanceStatus.Absent;

                if (existing != null)
                {
                    existing.Status = parsedStatus;
                    existing.Note = note;
                    _context.Update(existing);
                }
                else
                {
                    _context.AttendanceRecords.Add(new AttendanceRecord { UserId = userId, Date = date, Status = parsedStatus, Note = note });
                }
            }

            LogAction("تعديل حضور وانصراف", $"تم تحديث حالة الموظف ID:{userId} ليوم {date:yyyy-MM-dd} إلى {status}");
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم حفظ السجل بنجاح.";
            return RedirectToAction(nameof(Attendance), new { date = date.ToString("yyyy-MM-dd") });
        }


        // ==========================================
        // 📌 3. الرواتب (Payroll) والترحيل للحسابات
        // ==========================================
        public async Task<IActionResult> Payroll(string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");
            ViewBag.CurrentMonthYear = targetDate.ToString("yyyy-MM");
            int m = targetDate.Month;
            int y = targetDate.Year;

            var users = await _context.SystemUsers.ToListAsync();
            var attendanceThisMonth = await _context.AttendanceRecords.Where(a => a.Date.Month == m && a.Date.Year == y).ToListAsync();
            var savedPayrolls = await _context.PayrollRecords.Where(p => p.Month == m && p.Year == y).ToListAsync();

            var payrollList = new List<PayrollRecord>();

            foreach (var user in users)
            {
                var existingPayroll = savedPayrolls.FirstOrDefault(p => p.UserId == user.UserId);
                if (existingPayroll != null)
                {
                    existingPayroll.User = user;
                    payrollList.Add(existingPayroll);
                }
                else
                {
                    // حساب آلي بناء على الحضور
                    int present = attendanceThisMonth.Count(a => a.UserId == user.UserId && a.Status == AttendanceStatus.Present);
                    int absent = attendanceThisMonth.Count(a => a.UserId == user.UserId && a.Status == AttendanceStatus.Absent);

                    decimal dailyRate = user.BaseSalary > 0 ? (user.BaseSalary / 30m) : 0;
                    decimal deductions = absent * dailyRate;
                    decimal net = user.BaseSalary - deductions;

                    var newRecord = new PayrollRecord
                    {
                        UserId = user.UserId,
                        User = user,
                        Month = m,
                        Year = y,
                        BaseSalary = user.BaseSalary,
                        PresentDays = present,
                        AbsentDays = absent,
                        Deductions = deductions > 0 ? deductions : 0,
                        NetSalary = net > 0 ? net : 0,
                        IsPaid = false
                    };
                    payrollList.Add(newRecord);
                }
            }

            return View(payrollList);
        }

        [HttpPost]
        public async Task<IActionResult> ApproveAndPaySalary(int userId, int month, int year, decimal finalNet, PaymentMethod paymentMethod)
        {
            var user = await _context.SystemUsers.FindAsync(userId);
            if (user == null) return NotFound();

            var existing = await _context.PayrollRecords.FirstOrDefaultAsync(p => p.UserId == userId && p.Month == month && p.Year == year);
            if (existing != null && existing.IsPaid)
            {
                TempData["ErrorMessage"] = "تم صرف راتب هذا الموظف مسبقاً لهذا الشهر!";
                return RedirectToAction(nameof(Payroll), new { monthYear = $"{year}-{month:D2}" });
            }

            // 1. تحديث أو إنشاء سجل الراتب
            if (existing != null)
            {
                existing.NetSalary = finalNet;
                existing.IsPaid = true;
                existing.PaymentDate = DateTime.Now;
                _context.Update(existing);
            }
            else
            {
                var attendanceThisMonth = await _context.AttendanceRecords.Where(a => a.Date.Month == month && a.Date.Year == year && a.UserId == userId).ToListAsync();
                _context.PayrollRecords.Add(new PayrollRecord
                {
                    UserId = userId,
                    Month = month,
                    Year = year,
                    BaseSalary = user.BaseSalary,
                    PresentDays = attendanceThisMonth.Count(a => a.Status == AttendanceStatus.Present),
                    AbsentDays = attendanceThisMonth.Count(a => a.Status == AttendanceStatus.Absent),
                    Deductions = user.BaseSalary - finalNet,
                    NetSalary = finalNet,
                    IsPaid = true,
                    PaymentDate = DateTime.Now
                });
            }

            // 2. 📌 التسميع الفوري في الحسابات العامة (خصم من الخزنة وتقليل الأرباح)
            if (finalNet > 0)
            {
                // أ. إضافتها في المصروفات
                _context.Expenses.Add(new Expense
                {
                    Description = $"صرف راتب شهر {month}/{year} للموظف ({user.Username})",
                    Amount = finalNet,
                    DeductionFrom = DeductionSource.Company, // مصروف على الشركة
                    PaymentMethod = paymentMethod,
                    Date = DateTime.Now,
                    RecordedBy = User.Identity?.Name ?? "نظام الـ HR"
                });

                // ب. سحب الكاش من الدرج أو البنك
                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = finalNet,
                    Type = SafeTransactionType.DepositToBank, // خروج أموال
                    TargetSafe = SafeType.General,
                    PaymentMethod = paymentMethod,
                    Description = $"صرف راتب للموظف {user.Username}",
                    RecordedBy = User.Identity?.Name ?? "System",
                    Date = DateTime.Now
                });
            }

            LogAction("صرف راتب", $"تم اعتماد وصرف راتب الموظف {user.Username} لشهر {month} بقيمة {finalNet}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم صرف الراتب، وإرساله لقسم الحسابات، وخصمه من الخزنة بنجاح.";
            return RedirectToAction(nameof(Payroll), new { monthYear = $"{year}-{month:D2}" });
        }
    }
}