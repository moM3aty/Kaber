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
    [Authorize(Roles = "Admin,HR")]
    public class HRController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HRController(ApplicationDbContext context) { _context = context; }

        // 📌 1. لوحة تحكم HR (الإحصائيات والموظفين)
        public async Task<IActionResult> Index()
        {
            var employees = await _context.SystemUsers.OrderBy(u => u.Role).ToListAsync();
            ViewBag.TotalEmployees = employees.Count;
            ViewBag.ActiveLeaves = await _context.LeaveRequests.CountAsync(l => l.Status == LeaveStatus.Approved && DateTime.Now >= l.StartDate && DateTime.Now <= l.EndDate);
            ViewBag.PendingLeaves = await _context.LeaveRequests.CountAsync(l => l.Status == LeaveStatus.Pending);

            return View(employees);
        }

        // 📌 2. إدارة الإجازات
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
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تقديم طلب الإجازة بنجاح.";
            }
            return RedirectToAction(nameof(Leaves));
        }

        [HttpPost]
        public async Task<IActionResult> UpdateLeaveStatus(int id, LeaveStatus status, string note)
        {
            var leave = await _context.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == id);
            if (leave != null)
            {
                leave.Status = status;
                leave.AdminNote = note;

                // 💡 ربط ذكي: إذا كان الموظف "فني" وتمت الموافقة على إجازته، نحدث حالته في جدول الفنيين
                if (status == LeaveStatus.Approved)
                {
                    var tech = await _context.Technicians.FirstOrDefaultAsync(t => t.Name == leave.User.Username);
                    if (tech != null && DateTime.Now >= leave.StartDate && DateTime.Now <= leave.EndDate)
                    {
                        tech.IsAvailable = false; // تعطيل الفني تلقائياً أثناء الإجازة
                        _context.Update(tech);
                    }
                }

                _context.SystemLogs.Add(new SystemLog { ActionType = "قرار إجازة", Details = $"تم {status} إجازة الموظف {leave.User.Username}", Username = User.Identity?.Name });
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تحديث حالة الإجازة.";
            }
            return RedirectToAction(nameof(Leaves));
        }

        // 📌 3. مواعيد نزول المرتبات
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
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تحديد موعد صرف الرواتب بنجاح.";
            return RedirectToAction(nameof(Payroll));
        }
    }
}