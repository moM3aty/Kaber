using System.Linq;
using System.Threading.Tasks;
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
        public async Task<IActionResult> Create(SystemUser user)
        {
            if (ModelState.IsValid)
            {
                // التحقق من عدم تكرار اسم المستخدم
                if (await _context.SystemUsers.AnyAsync(u => u.Username == user.Username))
                {
                    ViewBag.Error = "اسم المستخدم هذا موجود مسبقاً!";
                    return View(user);
                }

                _context.Add(user);
                await _context.SaveChangesAsync();
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
        public async Task<IActionResult> Edit(int id, SystemUser user)
        {
            if (id != user.UserId) return NotFound();

            if (ModelState.IsValid)
            {
                _context.Update(user);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تحديث بيانات المستخدم بنجاح.";
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
                }
                else
                {
                    _context.SystemUsers.Remove(user);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "تم حذف المستخدم بنجاح.";
                }
            }
            return RedirectToAction(nameof(Index));
        }
    }
}