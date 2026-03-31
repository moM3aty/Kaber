using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace KaberSystem.Controllers
{
    // 📌 تم تحديث الصلاحيات لتشمل الصلاحية المرنة "ManageHR"
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

        // 📌 التحديث الأهم: ربط الإجازة بالحسابات وتطبيق الخصم
        [HttpPost]
        public async Task<IActionResult> UpdateLeaveStatus(int id, LeaveStatus status, string note, decimal deductionAmount)
        {
            var leave = await _context.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == id);
            if (leave != null)
            {
                leave.Status = status;
                leave.AdminNote = note;

                // البحث عن الفني المرتبط بهذا المستخدم
                var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.Name == leave.User.Username);

                if (status == LeaveStatus.Approved)
                {
                    if (tech != null)
                    {
                        tech.IsAvailable = false; // تعطيل الفني تلقائياً
                        _context.Update(tech);

                        // 📌 التسميع في الحسابات (خصم من الراتب)
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
                TempData["SuccessMessage"] = "تم اعتماد الإجازة وتسميعها في الحسابات بنجاح.";
            }
            return RedirectToAction(nameof(Leaves));
        }

        // 📌 إضافة دالة "مباشرة العمل"
        [HttpPost]
        public async Task<IActionResult> ReturnToWork(int leaveId)
        {
            var leave = await _context.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == leaveId);
            if (leave != null && leave.Status == LeaveStatus.Approved && !leave.IsReturned)
            {
                leave.IsReturned = true;
                leave.ActualReturnDate = DateTime.Now;
                _context.Update(leave);

                // إعادة تفعيل الفني ليصبح متاحاً للعمل واستلام الطلبات
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

        public async Task<IActionResult> Payroll()
        {
            var schedules = await _context.PayrollSchedules.OrderByDescending(s => s.Year).ThenByDescending(s => s.Month).ToListAsync();
            return View(schedules);
        }

        [HttpPost]
        public async Task<IActionResult> SetPayrollDate(int month, int year, DateTime date, string note)
        {
            var schedule = new PayrollSchedule { Month = month, Year = year, ScheduledDate = date, Note = note };
            _context.PayrollSchedules.Add(schedule);
            LogAction("جدولة رواتب", $"تم جدولة رواتب شهر {month}/{year} لتكون يوم {date:yyyy-MM-dd}");
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تحديد موعد صرف الرواتب بنجاح.";
            return RedirectToAction(nameof(Payroll));
        }
    }
}