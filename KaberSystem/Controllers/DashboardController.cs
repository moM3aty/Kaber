using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;

namespace KaberSystem.Controllers
{
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;

        public DashboardController(ApplicationDbContext context)
        {
            _context = context;
        }

        // الصفحة الرئيسية للوحة التحكم (لوحة القيادة الإحصائية للإدارة)
        public async Task<IActionResult> Index()
        {
            // إحصائيات الأرقام العلوية (KPIs)
            ViewData["TotalOrders"] = await _context.Orders.CountAsync();

            var newOrdersCount = await _context.Orders.CountAsync(o => o.Status == OrderStatus.New);
            var completedOrdersCount = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Completed);
            var inProgressOrdersCount = await _context.Orders.CountAsync(o => o.Status == OrderStatus.InProgress || o.Status == OrderStatus.Assigned);

            ViewData["NewOrders"] = newOrdersCount;
            ViewData["CompletedOrders"] = completedOrdersCount;
            ViewData["InProgressOrders"] = inProgressOrdersCount; // للرسم البياني

            // إحصائيات الفنيين
            ViewData["ActiveTechnicians"] = await _context.Technicians.CountAsync(t => t.IsAvailable);

            // إحصائيات مالية (مجموع الفواتير المدفوعة)
            ViewData["TotalRevenue"] = await _context.Invoices.Where(i => i.Status == InvoiceStatus.Paid).SumAsync(i => i.Amount);

            // جلب أحدث 10 طلبات لعرضها في الشاشة الرئيسية (تم زيادتها لدعم الجدول التفاعلي)
            var recentOrders = await _context.Orders
                .Include(o => o.Technician)
                .OrderByDescending(o => o.CreatedAt)
                .Take(10)
                .ToListAsync();

            return View(recentOrders);
        }
    }
}