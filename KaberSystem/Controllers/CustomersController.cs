using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using System.Dynamic;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,CallCenter")] // متاح للإدارة وخدمة العملاء
    public class CustomersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CustomersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 📌 شاشة قائمة العملاء (تجميع ذكي برقم الجوال)
        public async Task<IActionResult> Index(string searchQuery)
        {
            ViewData["CurrentSearch"] = searchQuery;

            var query = _context.Orders.AsQueryable();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(o => o.CustomerName.Contains(searchQuery) || o.PhoneNumber.Contains(searchQuery));
            }

            // 💡 تجميع الطلبات برقم الجوال لإنشاء "ملف عميل" فريد
            var customers = await query
                .GroupBy(o => new { o.PhoneNumber, o.CustomerName, o.Address })
                .Select(g => new
                {
                    Phone = g.Key.PhoneNumber,
                    Name = g.Key.CustomerName,
                    Address = g.Key.Address,
                    TotalOrders = g.Count(),
                    TotalSpent = g.Sum(o => o.FinalPrice),
                    LastOrderDate = g.Max(o => o.CreatedAt)
                })
                .OrderByDescending(c => c.LastOrderDate)
                .ToListAsync();

            // تحويل الـ Anonymous Type إلى ExpandoObject لسهولة تمريره للـ View
            var model = customers.Select(c =>
            {
                dynamic expando = new ExpandoObject();
                expando.Phone = c.Phone;
                expando.Name = c.Name;
                expando.Address = c.Address;
                expando.TotalOrders = c.TotalOrders;
                expando.TotalSpent = c.TotalSpent;
                expando.LastOrderDate = c.LastOrderDate;
                return expando;
            }).ToList();

            return View(model);
        }

        // 📌 ملف العميل التفصيلي (تاريخ الطلبات)
        public async Task<IActionResult> Details(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return NotFound();

            var customerOrders = await _context.Orders
                .Include(o => o.Technician)
                .Where(o => o.PhoneNumber == phone)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();

            if (!customerOrders.Any()) return NotFound();

            // إرسال بيانات العميل الأساسية للـ ViewBag
            ViewBag.CustomerName = customerOrders.First().CustomerName;
            ViewBag.CustomerPhone = phone;
            ViewBag.CustomerAddress = customerOrders.First().Address;
            ViewBag.TotalSpent = customerOrders.Sum(o => o.FinalPrice);
            ViewBag.TotalOrders = customerOrders.Count;

            return View(customerOrders);
        }
    }
}