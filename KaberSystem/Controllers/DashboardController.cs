// مسار الملف: Controllers/DashboardController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using System.Collections.Generic;

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
        public async Task<IActionResult> Index(string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");

            var monthlyOrders = await _context.Orders
               .Include(o => o.UsedSpareParts).ThenInclude(p => p.SparePart)
               .Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month)
               .ToListAsync();

            decimal totalRevenue = monthlyOrders.Sum(o => o.FinalPrice);
            decimal cogs = monthlyOrders.SelectMany(o => o.UsedSpareParts).Sum(p => p.QuantityUsed * (p.SparePart?.PurchasePrice ?? 0));

            // ج. إجمالي الربح المبدئي للشهر
            decimal grossProfit = totalRevenue - cogs;

            // د. المصروفات التشغيلية للشهر
            var opExpensesList = await _context.Expenses
                .Where(e => e.Date.Year == targetDate.Year && e.Date.Month == targetDate.Month)
                .ToListAsync();
            decimal totalOpExpenses = opExpensesList.Sum(e => e.Amount);

            // هـ. التوالف للشهر
            decimal damagesCost = await _context.DamagedParts
                .Where(d => d.Date.Year == targetDate.Year && d.Date.Month == targetDate.Month)
                .SumAsync(d => d.TotalLoss);

            // و. عمولات الفنيين المنصرفة في الشهر
            var techCommissionsTransactions = await _context.SafeTransactions
                .Where(t => t.Type == SafeTransactionType.DepositToBank && t.Date.Year == targetDate.Year && t.Date.Month == targetDate.Month
                         && t.Description.Contains("صرف صافي عمولة الفني"))
                .ToListAsync();
            decimal techCommissionsPaid = techCommissionsTransactions.Sum(t => t.Amount);

            // إجمالي المصروفات الشهرية
            decimal allExpenses = totalOpExpenses + damagesCost + techCommissionsPaid;
            decimal netProfit = grossProfit - allExpenses;

            // إحصائيات الأرقام العلوية (KPIs)
            ViewData["TotalOrders"] = await _context.Orders.CountAsync();

            var newOrdersCount = await _context.Orders.CountAsync(o => o.Status == OrderStatus.New);
            var completedOrdersCount = await _context.Orders.CountAsync(o => o.Status == OrderStatus.Completed);
            var inProgressOrdersCount = await _context.Orders.CountAsync(o => o.Status == OrderStatus.InProgress || o.Status == OrderStatus.Assigned);

            ViewData["NewOrders"] = newOrdersCount;
            ViewData["CompletedOrders"] = completedOrdersCount;
            ViewData["InProgressOrders"] = inProgressOrdersCount; // للرسم البياني الدائري

            // إحصائيات الفنيين
            ViewData["ActiveTechnicians"] = await _context.Technicians.CountAsync(t => t.IsAvailable);

            // إحصائيات مالية (صافي الربح)
            ViewData["TotalRevenue"] = netProfit;


            // 📌 التحديث الجديد: حساب الإيرادات الفعلية لآخر 6 أشهر لتغذية الرسم البياني
            var chartLabels = new List<string>();
            var chartData = new List<decimal>();

            for (int i = 5; i >= 0; i--)
            {
                var loopDate = targetDate.AddMonths(-i);

                // جلب اسم الشهر بالعربي
                string monthName = loopDate.ToString("MMMM", new System.Globalization.CultureInfo("ar-SA"));
                chartLabels.Add(monthName);

                // حساب إجمالي مبيعات/إيرادات هذا الشهر
                decimal monthRev = await _context.Orders
                    .Where(o => o.IsPaid && o.CreatedAt.Year == loopDate.Year && o.CreatedAt.Month == loopDate.Month)
                    .SumAsync(o => o.FinalPrice);

                chartData.Add(monthRev);
            }

            ViewData["ChartLabels"] = chartLabels;
            ViewData["ChartData"] = chartData;
            // -------------------------------------------------------------


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