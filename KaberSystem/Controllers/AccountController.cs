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

                if (role == "Technician") return Redirect("/Technicians/MyStock");
                if (role == "Store" || role == "Inventory") return Redirect("/Inventory/Index");
                if (role == "Accounting") return Redirect("/Accounting/Index");
                if (role == "CallCenter" || role == "Orders") return Redirect("/Orders/Index");

                // 📌 التحديث: توجيه مديري ومندوبي المشتريات لصفحتهم عند الدخول
                if (role == "PurchasingManager" || role == "PurchasingRep" || role == "Purchasing")
                    return Redirect("/Purchases/Index");

                return Redirect("/Dashboard/Index");
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

                // 📌 التوجيه المباشر والآمن حسب الصلاحية
                if (userRole.Equals("Technician", StringComparison.OrdinalIgnoreCase))
                    return Redirect("/Technicians/MyStock");

                if (userRole.Equals("Store", StringComparison.OrdinalIgnoreCase) || claims.Any(c => c.Value == "Inventory"))
                    return Redirect("/Inventory/Index");

                if (userRole.Equals("Accounting", StringComparison.OrdinalIgnoreCase))
                    return Redirect("/Accounting/Index");

                if (userRole.Equals("CallCenter", StringComparison.OrdinalIgnoreCase) || claims.Any(c => c.Value == "Orders"))
                    return Redirect("/Orders/Index");

                // 📌 التحديث: إضافة توجيه المشتريات
                if (userRole.Equals("PurchasingManager", StringComparison.OrdinalIgnoreCase) ||
                    userRole.Equals("PurchasingRep", StringComparison.OrdinalIgnoreCase) ||
                    claims.Any(c => c.Value == "Purchasing"))
                    return Redirect("/Purchases/Index");

                return Redirect("/Dashboard/Index");
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