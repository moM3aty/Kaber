using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;

namespace KaberSystem.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        public AccountController(ApplicationDbContext context) { _context = context; }

        public IActionResult Login()
        {
            if (User.Identity.IsAuthenticated)
                return RedirectToAction("Index", "Dashboard");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(string username, string password)
        {
            var user = await _context.SystemUsers
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower() && u.Password == password);

            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role) // الدور الأساسي
                };

                // 📌 التحديث السحري الجذري هنا: حقن الصلاحيات الإضافية كأدوار (Roles) في جلسة المستخدم
                if (!string.IsNullOrEmpty(user.Permissions))
                {
                    // تقسيم الصلاحيات (التي تم حفظها مفصولة بفاصلة)
                    var extraPermissions = user.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var perm in extraPermissions)
                    {
                        // إضافة كل صلاحية إضافية كـ Role جديد للمتصفح
                        claims.Add(new Claim(ClaimTypes.Role, perm.Trim()));
                    }
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                // التوجيه الذكي
                if (user.Role == "Technician") return RedirectToAction("Index", "Orders");
                if (user.Role == "Store") return RedirectToAction("Index", "Inventory");
                if (user.Role == "Accounting") return RedirectToAction("Index", "Accounting");

                return RedirectToAction("Index", "Dashboard");
            }

            ViewBag.Error = "خطأ في اسم المستخدم أو كلمة المرور!";
            return View();
        }

        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}