using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,Accounting")] // متاح للإدارة والمحاسبين فقط
    public class AccountingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountingController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // إجمالي الإيرادات المحصلة
            decimal totalRevenue = await _context.Orders.Where(o => o.IsPaid).SumAsync(o => o.FinalPrice);

            // إجمالي مصروفات الشركة فقط (التي لا تُخصم من الفني)
            decimal companyExpenses = await _context.Expenses
                .Where(e => e.DeductionFrom == DeductionSource.Company)
                .SumAsync(e => e.Amount);

            // إجمالي التوالف
            decimal totalDamages = await _context.DamagedParts.SumAsync(d => d.TotalLoss);

            // التقارير التفصيلية للفنيين
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Expenses)
                .ToListAsync();

            var techReports = technicians.Select(t => new {
                Id = t.TechnicianId,
                Name = t.Name,
                GrossIncome = t.AssignedOrders.Where(o => o.IsPaid).Sum(o => o.FinalPrice),

                // الخصومات الشخصية فقط (سلف، جزاءات)
                PersonalDeductions = t.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount),

                // صافي ربح الفني = دخله - خصوماته الشخصية
                NetProfit = t.AssignedOrders.Where(o => o.IsPaid).Sum(o => o.FinalPrice) -
                             t.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount),

                WalletBalance = t.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice)
            }).ToList();

            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.TotalExpenses = companyExpenses; // مصروفات الشركة فقط
            ViewBag.TotalDamagesCost = totalDamages;
            ViewBag.NetCompanyProfit = totalRevenue - companyExpenses - totalDamages;
            ViewBag.TechnicianReports = techReports;

            return View();
        }

        // 📌 2. إدارة الفواتير (تأكيد / إلغاء)
        public async Task<IActionResult> Invoices()
        {
            var invoices = await _context.Invoices
                .Include(i => i.Order)
                    .ThenInclude(o => o.Technician)
                .OrderByDescending(i => i.IssuedAt)
                .ToListAsync();

            return View(invoices);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateInvoiceStatus(int invoiceId, InvoiceStatus newStatus)
        {
            var invoice = await _context.Invoices.Include(i => i.Order).FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);
            if (invoice != null)
            {
                invoice.Status = newStatus;

                // تسجيل العملية (Log)
                _context.SystemLogs.Add(new SystemLog { ActionType = "تحديث فاتورة", Details = $"تم تغيير حالة الفاتورة #{invoice.InvoiceId} للطلب #{invoice.OrderId} إلى {newStatus}", Username = User.Identity?.Name });

                // إذا تم تأكيد الدفع، نقوم بتحديث حالة الطلب إلى (تم الدفع)
                if (newStatus == InvoiceStatus.Paid && invoice.Order != null)
                {
                    invoice.Order.IsPaid = true;
                }

                _context.Update(invoice);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تحديث حالة الفاتورة بنجاح.";
            }
            return RedirectToAction(nameof(Invoices));
        }

        // 📌 3. إدارة المصروفات والرواتب
        public async Task<IActionResult> Expenses()
        {
            ViewData["TechnicianId"] = new SelectList(await _context.Technicians.ToListAsync(), "TechnicianId", "Name");
            var expenses = await _context.Expenses.Include(e => e.Technician).OrderByDescending(e => e.Date).ToListAsync();
            return View(expenses);
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddExpense(string Description, decimal Amount, int? TechnicianId, DeductionSource DeductionFrom)
        {
            var expense = new Expense
            {
                Description = Description,
                Amount = Amount,
                TechnicianId = TechnicianId,
                DeductionFrom = DeductionFrom,
                Date = DateTime.Now,
                RecordedBy = User.Identity?.Name ?? "System"
            };

            _context.Expenses.Add(expense);

            // تسجيل اللوج
            string target = DeductionFrom == DeductionSource.Company ? "حساب الشركة" : "حساب الفني";
            _context.SystemLogs.Add(new SystemLog
            {
                ActionType = "صرف مالي",
                Details = $"تم صرف {Amount} ريال ({Description}) مخصومة من {target}",
                Username = User.Identity?.Name
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تسجيل القيد المالي بنجاح.";
            return RedirectToAction(nameof(Expenses));
        }

        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> DeleteExpense(int id)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense != null)
            {
                _context.Expenses.Remove(expense);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف قيد المصروف.";
            }
            return RedirectToAction(nameof(Expenses));
        }
        [HttpPost]
        public async Task<IActionResult> SettleTechCash(int techId, decimal amount, string note)
        {
            var tech = await _context.Technicians.FindAsync(techId);
            if (tech == null) return NotFound();

            _context.SafeTransactions.Add(new SafeTransaction
            {
                Amount = amount,
                Type = SafeTransactionType.Income,
                Description = $"توريد كاش من الفني {tech.Name}: {note}",
                RecordedBy = User.Identity?.Name ?? "System",
                Date = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم توريد الكاش للخزنة.";
            return RedirectToAction(nameof(Index));
        }
    }
}