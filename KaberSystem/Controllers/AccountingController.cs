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
    [Authorize(Roles = "Admin,Accounting")]
    public class AccountingController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AccountingController(ApplicationDbContext context)
        {
            _context = context;
        }

        private void LogAction(string actionType, string details)
        {
            var username = User.Identity?.Name ?? "مستخدم غير معروف";
            var role = User.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value ?? "صلاحية غير محددة";

            _context.SystemLogs.Add(new SystemLog
            {
                ActionType = actionType,
                Details = details,
                Username = $"{username} - [{role}]",
                Timestamp = DateTime.Now
            });
        }

        public async Task<IActionResult> Index()
        {
            decimal totalRevenue = await _context.Orders.Where(o => o.IsPaid).SumAsync(o => o.FinalPrice);

            decimal dailyIncome = await _context.Orders
                .Where(o => o.IsPaid && o.CreatedAt.Date == DateTime.Now.Date)
                .SumAsync(o => o.FinalPrice);

            decimal companyExpenses = await _context.Expenses
                .Where(e => e.DeductionFrom == DeductionSource.Company)
                .SumAsync(e => e.Amount);

            decimal totalDamages = await _context.DamagedParts.SumAsync(d => d.TotalLoss);

            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Expenses)
                .ToListAsync();

            var techReports = technicians.Select(t => new {
                Id = t.TechnicianId,
                Name = t.Name,
                GrossIncome = t.AssignedOrders.Where(o => o.IsPaid).Sum(o => o.FinalPrice),
                TotalDeductions = t.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount),
                NetProfit = t.AssignedOrders.Where(o => o.IsPaid).Sum(o => o.FinalPrice) -
                             t.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount),
                WalletBalance = t.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice)
            }).ToList();

            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.DailyIncome = dailyIncome;
            ViewBag.TotalExpenses = companyExpenses;
            ViewBag.TotalDamagesCost = totalDamages;
            ViewBag.NetCompanyProfit = totalRevenue - companyExpenses - totalDamages;
            ViewBag.TechnicianReports = techReports;

            return View();
        }

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

                if (newStatus == InvoiceStatus.Paid && invoice.Order != null)
                {
                    invoice.Order.IsPaid = true;
                }

                _context.Update(invoice);
                LogAction("تحديث فاتورة", $"تم تغيير حالة الفاتورة #{invoice.InvoiceId} للطلب #{invoice.OrderId} إلى {newStatus}");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تحديث حالة الفاتورة بنجاح.";
            }
            return RedirectToAction(nameof(Invoices));
        }

        // 📌 الدالة الجديدة والسحرية: استرداد الفاتورة وخصمها من الخزنة
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefundInvoice(int invoiceId, string refundReason)
        {
            var invoice = await _context.Invoices.Include(i => i.Order).FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

            if (invoice != null && invoice.Status == InvoiceStatus.Paid)
            {
                // 1. تغيير حالة الفاتورة لتصبح مرفوضة/ملغاة
                invoice.Status = InvoiceStatus.Rejected;
                _context.Update(invoice);

                // 2. تحديث الطلب ليكون "غير مدفوع"
                if (invoice.Order != null)
                {
                    invoice.Order.IsPaid = false;
                    invoice.Order.PaymentMethod = PaymentMethod.None;
                    _context.Update(invoice.Order);
                }

                // 3. البحث عن الإيداع (الكاش) في الخزنة وعكسه (Refund)
                var originalSafeTransaction = await _context.SafeTransactions
                    .FirstOrDefaultAsync(s => s.OrderId == invoice.OrderId && s.Type == SafeTransactionType.Income);

                if (originalSafeTransaction != null)
                {
                    // نقوم بإنشاء قيد "خروج أموال" لخصم المبلغ من الخزنة لضبط الرصيد
                    var refundTransaction = new SafeTransaction
                    {
                        Amount = originalSafeTransaction.Amount,
                        Type = SafeTransactionType.DepositToBank, // نوع الحركة (خروج)
                        Description = $"استرداد مالي (إلغاء فاتورة #{invoiceId}). السبب: {refundReason}",
                        OrderId = invoice.OrderId,
                        Date = DateTime.Now,
                        RecordedBy = User.Identity?.Name ?? "Accounting"
                    };
                    _context.SafeTransactions.Add(refundTransaction);
                }

                // 4. تسجيل العملية بدقة
                LogAction("استرداد فاتورة", $"تم إلغاء الدفع واسترداد الفاتورة #{invoiceId} للطلب #{invoice.OrderId}. السبب: {refundReason}");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم إلغاء الفاتورة واسترداد المبلغ من الخزنة بنجاح.";
            }
            else
            {
                TempData["ErrorMessage"] = "حدث خطأ! الفاتورة غير موجودة أو أنها ليست مدفوعة.";
            }

            return RedirectToAction(nameof(Invoices));
        }

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

            string target = DeductionFrom == DeductionSource.Company ? "حساب الشركة" : "حساب الفني";
            LogAction("تسجيل قيد مالي", $"تم صرف {Amount} ريال ({Description}) مخصومة من {target}");

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
                LogAction("حذف قيد مالي", $"تم حذف القيد المالي: ({expense.Description}) بقيمة {expense.Amount} ريال");
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

            LogAction("تصفية كاش فني", $"تم توريد مبلغ {amount} ريال كاش من الفني {tech.Name}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم توريد الكاش للخزنة.";
            return RedirectToAction(nameof(Index));
        }
    }
}