using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
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
            // 📌 قراءة البيانات من قاعدة البيانات بدلاً من القاموس الثابت
            var user = await _context.SystemUsers
                .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower() && u.Password == password);

            if (user != null)
            {
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                };

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