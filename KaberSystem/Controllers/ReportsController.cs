// مسار الملف: Controllers/ReportsController.cs
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,Reports")]
    public class ReportsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ReportsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 📌 التحديث: تقرير حصر قطع الغيار والأكثر استخداماً
        public async Task<IActionResult> SpareParts()
        {
            // تجميع القطع التي تم استهلاكها في الطلبات للحصول على الأكثر استخداماً والأرباح
            var topParts = await _context.UsedSpareParts
                .Include(u => u.SparePart)
                .Where(u => u.Order != null && u.Order.Status != OrderStatus.Cancelled)
                .GroupBy(u => new { u.PartId, u.SparePart.Name, u.SparePart.PartCode, u.SparePart.PurchasePrice, u.SparePart.TaxAmount })
                .Select(g => new
                {
                    PartName = g.Key.Name,
                    PartCode = g.Key.PartCode,
                    TotalQuantityUsed = g.Sum(x => x.QuantityUsed),
                    TotalCost = g.Sum(x => x.QuantityUsed) * g.Key.PurchasePrice,
                    TotalTax = g.Sum(x => x.QuantityUsed) * g.Key.TaxAmount, // 📌 إضافة الضريبة التراكمية للقطعة
                    TotalSales = g.Sum(x => x.QuantityUsed * x.SellingPriceAtTime),
                    NetProfit = g.Sum(x => x.QuantityUsed * x.SellingPriceAtTime) - (g.Sum(x => x.QuantityUsed) * g.Key.PurchasePrice) - (g.Sum(x => x.QuantityUsed) * g.Key.TaxAmount)
                })
                .OrderByDescending(x => x.TotalQuantityUsed)
                .ToListAsync();

            ViewBag.TotalPartsSold = topParts.Sum(x => x.TotalQuantityUsed);
            ViewBag.TotalPartsSales = topParts.Sum(x => x.TotalSales);
            ViewBag.TotalPartsProfit = topParts.Sum(x => x.NetProfit);

            return View(topParts);
        }
    }
}