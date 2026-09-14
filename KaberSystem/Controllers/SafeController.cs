// مسار الملف: Controllers/SafeController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,Accounting")]
    public class SafeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SafeController(ApplicationDbContext context)
        {
            _context = context;
        }

        private void LogAction(string actionType, string details)
        {
            var username = User.Identity?.Name ?? "مستخدم غير معروف";
            _context.SystemLogs.Add(new SystemLog
            {
                ActionType = actionType,
                Details = details,
                Username = username,
                Timestamp = DateTime.Now
            });
        }

        // 📌 دالة تصفير وتسوية الدرج إلى 475 بناءً على طلبك
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ForceFixSafe()
        {
            var allCashTransactions = await _context.SafeTransactions
                .Include(s => s.Order)
                .Where(s => s.PaymentMethod == PaymentMethod.Cash || s.PaymentMethod == PaymentMethod.None)
                .ToListAsync();

            decimal handovers = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.Income && t.Description != null && (t.Description.Contains("توريد عهدة نقدية") || t.Description.Contains("استلام وتوريد كاش") || t.Description.Contains("مزامنة النظام") || t.Description.Contains("تسوية وتصفير"))).Sum(t => t.Amount);
            decimal adminInvoices = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.Income && t.OrderId.HasValue && t.Order != null && t.Order.TechnicianId == null).Sum(t => t.Amount);
            decimal manualAdjustments = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.Income && !t.OrderId.HasValue && !(t.Description != null && (t.Description.Contains("توريد عهدة نقدية") || t.Description.Contains("استلام وتوريد كاش") || t.Description.Contains("مزامنة النظام") || t.Description.Contains("تسوية وتصفير")))).Sum(t => t.Amount);
            decimal generalOutflows = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);

            decimal currentBalance = handovers + adminInvoices + manualAdjustments - generalOutflows;

            if (currentBalance != 475m)
            {
                decimal difference = 475m - currentBalance;
                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = Math.Abs(difference),
                    Type = difference > 0 ? SafeTransactionType.Income : SafeTransactionType.DepositToBank,
                    TargetSafe = SafeType.General,
                    PaymentMethod = PaymentMethod.Cash,
                    Description = $"[تسوية وتصفير نهائي]: ضبط الرصيد الافتتاحي للدرج إلى 475 ريال",
                    RecordedBy = "System Admin",
                    Date = DateTime.Now
                });
                await _context.SaveChangesAsync();
            }

            return Content("تم ضبط رصيد الخزنة العامة على (475 ريال) بنجاح. يمكنك العودة للنظام.");
        }


        public async Task<IActionResult> Index()
        {
            var transactions = await _context.SafeTransactions
                .Include(s => s.Order)
                .OrderByDescending(s => s.Date)
                .ToListAsync();

            var allCashTransactions = transactions.Where(t => t.PaymentMethod == PaymentMethod.Cash || t.PaymentMethod == PaymentMethod.None).ToList();

            decimal handovers = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.Income && t.Description != null && (t.Description.Contains("توريد عهدة نقدية") || t.Description.Contains("استلام وتوريد كاش") || t.Description.Contains("مزامنة النظام") || t.Description.Contains("تسوية وتصفير"))).Sum(t => t.Amount);
            decimal adminInvoices = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.Income && t.OrderId.HasValue && t.Order != null && t.Order.TechnicianId == null).Sum(t => t.Amount);
            decimal manualAdjustments = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.Income && !t.OrderId.HasValue && !(t.Description != null && (t.Description.Contains("توريد عهدة نقدية") || t.Description.Contains("استلام وتوريد كاش") || t.Description.Contains("مزامنة النظام") || t.Description.Contains("تسوية وتصفير")))).Sum(t => t.Amount);

            decimal realIncomes = handovers + adminInvoices + manualAdjustments;
            decimal realOutflows = allCashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);

            decimal currentBalance = realIncomes - realOutflows;

            ViewBag.CurrentBalance = currentBalance;
            ViewBag.TotalIncome = realIncomes;
            ViewBag.TotalDeposits = realOutflows;

            return View(transactions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DepositMoney(decimal amount, string description)
        {
            if (amount <= 0)
            {
                TempData["ErrorMessage"] = "يجب أن يكون المبلغ أكبر من صفر.";
                return RedirectToAction(nameof(Index));
            }

            var transaction = new SafeTransaction
            {
                Amount = amount,
                Type = SafeTransactionType.DepositToBank,
                Description = description,
                RecordedBy = User.Identity?.Name ?? "System",
                Date = DateTime.Now
            };

            _context.SafeTransactions.Add(transaction);
            LogAction("توريد أموال من الخزنة", $"تم توريد مبلغ {amount} ريال من الخزنة إلى البنك. البيان: {description}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم توريد الأموال وخصمها من الخزنة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> EditTransaction(int id, decimal amount, string description, SafeTransactionType type)
        {
            var transaction = await _context.SafeTransactions.FindAsync(id);
            if (transaction == null) return NotFound();

            if (transaction.OrderId.HasValue && transaction.Amount != amount)
            {
                TempData["ErrorMessage"] = "لا يمكن تغيير مبلغ حركة مرتبطة بفاتورة صيانة من هنا. استخدم زر (استرداد) من شاشة الفواتير.";
                return RedirectToAction(nameof(Index));
            }

            string oldDesc = transaction.Description;
            decimal oldAmount = transaction.Amount;

            transaction.Amount = amount;
            transaction.Description = description;

            if (!transaction.OrderId.HasValue)
            {
                transaction.Type = type;
            }

            _context.Update(transaction);
            LogAction("تعديل حركة خزنة", $"تعديل حركة #{id} من ({oldDesc} - {oldAmount}) إلى ({description} - {amount})");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم تعديل الحركة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteTransaction(int id)
        {
            var transaction = await _context.SafeTransactions.FindAsync(id);
            if (transaction != null)
            {
                if (transaction.OrderId.HasValue)
                {
                    TempData["ErrorMessage"] = "لا يمكن حذف حركة إيداع خاصة بفاتورة صيانة! قم بإلغاء الفاتورة من شاشة (إدارة الفواتير) بدلاً من ذلك.";
                    return RedirectToAction(nameof(Index));
                }

                _context.SafeTransactions.Remove(transaction);
                LogAction("حذف حركة خزنة", $"تم حذف حركة #{transaction.Id} بقيمة {transaction.Amount} ({transaction.Description})");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم حذف الحركة وإعادة ضبط رصيد الخزنة بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}