using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin")] // هذه الصفحة للأدمن فقط
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public UsersController(ApplicationDbContext context) { _context = context; }

        // 📌 دالة مساعدة لتسجيل الـ Logs في قاعدة البيانات
        private async Task LogActionAsync(string actionType, string details)
        {
            var username = User.Identity?.Name ?? "SystemAdmin";
            var log = new SystemLog
            {
                Username = username,
                ActionType = actionType,
                Details = details,
                Timestamp = DateTime.Now
            };
            _context.SystemLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        // عرض قائمة المستخدمين
        public async Task<IActionResult> Index()
        {
            var users = await _context.SystemUsers.OrderByDescending(u => u.CreatedAt).ToListAsync();
            return View(users);
        }

        // إضافة مستخدم جديد
        public IActionResult Create() => View();

        [HttpPost]
        [ValidateAntiForgeryToken]
        // 📌 تم إضافة SelectedPermissions لاستقبال الصلاحيات من الـ Checkboxes
        public async Task<IActionResult> Create(SystemUser user, List<string> SelectedPermissions)
        {
            if (ModelState.IsValid)
            {
                if (await _context.SystemUsers.AnyAsync(u => u.Username == user.Username))
                {
                    ViewBag.Error = "اسم المستخدم هذا موجود مسبقاً!";
                    return View(user);
                }

                // دمج الصلاحيات المختارة في نص واحد مفصول بفاصلة
                user.Permissions = SelectedPermissions != null ? string.Join(",", SelectedPermissions) : "";

                _context.Add(user);
                await _context.SaveChangesAsync();

                // 🔴 تسجيل العملية في الـ Log
                await LogActionAsync("إضافة مستخدم", $"تم إنشاء حساب جديد باسم: {user.Username} بصلاحية {user.Role}");

                TempData["SuccessMessage"] = "تم إضافة المستخدم بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        // تعديل مستخدم
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var user = await _context.SystemUsers.FindAsync(id);
            if (user == null) return NotFound();
            return View(user);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, SystemUser user, List<string> SelectedPermissions)
        {
            if (id != user.UserId) return NotFound();

            if (ModelState.IsValid)
            {
                var existingUser = await _context.SystemUsers.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == id);

                // تحديث الصلاحيات المخصصة
                user.Permissions = SelectedPermissions != null ? string.Join(",", SelectedPermissions) : "";

                _context.Update(user);
                await _context.SaveChangesAsync();

                // 🔴 تسجيل التعديل في الـ Log
                string changes = $"تم تعديل بيانات المستخدم: {user.Username}.";
                if (existingUser?.Role != user.Role) changes += $" تم تغيير الوظيفة إلى {user.Role}.";
                await LogActionAsync("تعديل صلاحيات", changes);

                TempData["SuccessMessage"] = "تم تحديث بيانات المستخدم وصلاحياته بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            return View(user);
        }

        // حذف مستخدم
        public async Task<IActionResult> Delete(int id)
        {
            var user = await _context.SystemUsers.FindAsync(id);
            if (user != null)
            {
                if (user.Username.ToLower() == "admin")
                {
                    TempData["ErrorMessage"] = "لا يمكن حذف المدير الرئيسي للنظام!";
                    await LogActionAsync("محاولة حذف مرفوضة", $"محاولة لحذف حساب الأدمن الرئيسي من قبل {User.Identity?.Name}");
                }
                else
                {
                    _context.SystemUsers.Remove(user);
                    await _context.SaveChangesAsync();

                    // 🔴 تسجيل الحذف في الـ Log
                    await LogActionAsync("حذف مستخدم", $"تم حذف حساب المستخدم: {user.Username} نهائياً.");

                    TempData["SuccessMessage"] = "تم حذف المستخدم بنجاح.";
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // 📌 صفحة عرض سجل العمليات (Logs) للأدمن
        public async Task<IActionResult> SystemLogs()
        {
            var logs = await _context.SystemLogs.OrderByDescending(l => l.Timestamp).Take(200).ToListAsync();
            return View(logs);
        }
    }
}