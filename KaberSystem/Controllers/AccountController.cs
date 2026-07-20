// مسار الملف: Controllers/AccountController.cs
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

        public AccountController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                var role = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role)?.Value ?? "";

                // 📌 التحديث: توجيه كل صلاحية لمكانها الصحيح إذا كان مسجل دخول مسبقاً
                if (role.Equals("Technician", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("MyStock", "Technicians");

                if (role.Equals("Store", StringComparison.OrdinalIgnoreCase) || User.Claims.Any(c => c.Value == "Inventory"))
                    return RedirectToAction("Index", "Inventory");

                if (role.Equals("Accounting", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Index", "Accounting");

                if (role.Equals("CallCenter", StringComparison.OrdinalIgnoreCase) || User.Claims.Any(c => c.Value == "Orders"))
                    return RedirectToAction("Index", "Orders");

                if (role.Equals("PurchasingManager", StringComparison.OrdinalIgnoreCase) ||
                    role.Equals("PurchasingRep", StringComparison.OrdinalIgnoreCase) ||
                    User.Claims.Any(c => c.Value == "Purchasing"))
                    return RedirectToAction("Index", "Purchases");

                if (role.Equals("HR", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Index", "HR");

                // مدير النظام يذهب للرئيسية
                return RedirectToAction("Index", "Dashboard");
            }
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string username, string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ViewBag.Error = "الرجاء إدخال اسم المستخدم وكلمة المرور!";
                return View();
            }

            string cleanUsername = username.Trim().ToLower();
            string cleanPassword = password.Trim();

            var allUsers = await _context.SystemUsers.ToListAsync();

            var user = allUsers.FirstOrDefault(u =>
                u.Username.Trim().ToLower() == cleanUsername &&
                u.Password.Trim() == cleanPassword);

            if (user != null)
            {
                string userRole = user.Role.Trim();

                if (userRole.Equals("Technician", StringComparison.OrdinalIgnoreCase))
                {
                    var allTechs = await _context.Technicians.ToListAsync();
                    bool techExists = allTechs.Any(t => t.Name.Trim().ToLower() == user.Username.Trim().ToLower());

                    if (!techExists)
                    {
                        ViewBag.Error = "حسابك لا يحتوي على ملف (فني) مرتبط. الرجاء مراجعة الإدارة لإنشاء فني يحمل نفس اسم الدخول.";
                        return View();
                    }
                }

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username.Trim()),
                    new Claim(ClaimTypes.Role, userRole)
                };

                if (!string.IsNullOrEmpty(user.Permissions))
                {
                    var extraPermissions = user.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var perm in extraPermissions)
                    {
                        string cleanPerm = perm.Trim();
                        if (!string.IsNullOrEmpty(cleanPerm))
                        {
                            claims.Add(new Claim(ClaimTypes.Role, cleanPerm));
                        }
                    }
                }

                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var principal = new ClaimsPrincipal(identity);

                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
                };

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);

                _context.SystemLogs.Add(new SystemLog
                {
                    ActionType = "تسجيل دخول",
                    Details = $"تسجيل دخول ناجح",
                    Username = $"{user.Username} - [{userRole}]",
                    Timestamp = DateTime.Now
                });
                await _context.SaveChangesAsync();

                // 📌 التحديث: التوجيه المباشر والدقيق حسب كل منصب بعد الدخول الناجح
                if (userRole.Equals("Technician", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("MyStock", "Technicians");

                if (userRole.Equals("Store", StringComparison.OrdinalIgnoreCase) || claims.Any(c => c.Value == "Inventory"))
                    return RedirectToAction("Index", "Inventory");

                if (userRole.Equals("Accounting", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Index", "Accounting");

                if (userRole.Equals("CallCenter", StringComparison.OrdinalIgnoreCase) || claims.Any(c => c.Value == "Orders"))
                    return RedirectToAction("Index", "Orders");

                if (userRole.Equals("PurchasingManager", StringComparison.OrdinalIgnoreCase) ||
                    userRole.Equals("PurchasingRep", StringComparison.OrdinalIgnoreCase) ||
                    claims.Any(c => c.Value == "Purchasing"))
                    return RedirectToAction("Index", "Purchases");

                if (userRole.Equals("HR", StringComparison.OrdinalIgnoreCase))
                    return RedirectToAction("Index", "HR");

                // الإدارة (Admin) تذهب للداش بورد
                return RedirectToAction("Index", "Dashboard");
            }

            bool usernameExists = allUsers.Any(u => u.Username.Trim().ToLower() == cleanUsername);
            if (usernameExists)
            {
                ViewBag.Error = "اسم المستخدم صحيح، لكن كلمة المرور خاطئة.";
            }
            else
            {
                ViewBag.Error = "اسم المستخدم غير موجود في النظام.";
            }

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Logout()
        {
            var username = User.Identity?.Name ?? "مستخدم";

            _context.SystemLogs.Add(new SystemLog
            {
                ActionType = "تسجيل خروج",
                Details = "تم تسجيل الخروج بنجاح",
                Username = username,
                Timestamp = DateTime.Now
            });
            await _context.SaveChangesAsync();

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login");
        }
    }
}