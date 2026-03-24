using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;

namespace KaberSystem.Controllers
{
    public class AccountingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountingController(ApplicationDbContext context)
        {
            _context = context;
        }

        // عرض شاشة الحسابات والفواتير الصادرة
        public async Task<IActionResult> Index()
        {
            // جلب جميع الفواتير مع ربطها ببيانات الطلب والعميل
            var invoices = await _context.Invoices
                .Include(i => i.Order)
                .OrderByDescending(i => i.IssuedAt)
                .ToListAsync();

            // حساب الإحصائيات المالية للوحة الحسابات
            ViewData["TotalIncome"] = invoices.Where(i => i.Status == InvoiceStatus.Paid).Sum(i => i.Amount);
            ViewData["PendingPayments"] = invoices.Where(i => i.Status == InvoiceStatus.NotReceived).Sum(i => i.Amount);

            return View(invoices);
        }

        // تغيير حالة الفاتورة (مثلاً من لم يتم الاستلام إلى تم الدفع)
        [HttpPost]
        public async Task<IActionResult> MarkAsPaid(int id)
        {
            var invoice = await _context.Invoices.FindAsync(id);
            if (invoice != null)
            {
                invoice.Status = InvoiceStatus.Paid;
                _context.Update(invoice);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تأكيد عملية الدفع وتحصيل الفاتورة بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}