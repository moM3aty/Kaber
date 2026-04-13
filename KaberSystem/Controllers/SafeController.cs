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
            var transactions = await _context.SafeTransactions
                .Include(s => s.Order)
                .OrderByDescending(s => s.Date)
                .ToListAsync();

            // 📌 التحديث الجذري: حساب السيولة النقدية الفعلية (الكاش) في الدرج فقط لمنع الدبلرة

            // 1. الفلترة لسحب الحركات الكاش فقط
            var cashTransactions = transactions.Where(t => t.PaymentMethod == PaymentMethod.Cash || t.PaymentMethod == PaymentMethod.None).ToList();

            // 2. استبعاد أموال الفواتير التي لا تزال في جيب الفني (لم تورد للدرج بعد)
            var actualDrawerIncome = cashTransactions.Where(t =>
                t.Type == SafeTransactionType.Income &&
                !(t.OrderId.HasValue && (t.Description.Contains("تحصيل أجور") || t.Description.Contains("استرداد رأس مال")))
            ).ToList();

            var actualDrawerDeposits = cashTransactions.Where(t => t.Type == SafeTransactionType.DepositToBank).ToList();

            // 3. حساب الصافي الفعلي للدرج
            decimal totalIncome = actualDrawerIncome.Sum(t => t.Amount);
            decimal totalDeposits = actualDrawerDeposits.Sum(t => t.Amount);

            ViewBag.CurrentBalance = totalIncome - totalDeposits;
            ViewBag.TotalIncome = totalIncome;
            ViewBag.TotalDeposits = totalDeposits;

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

            // 📌 التأكد من الرصيد الفعلي قبل التوريد (بناءً على المعادلة الجديدة)
            var allCash = await _context.SafeTransactions
                .Where(t => t.PaymentMethod == PaymentMethod.Cash || t.PaymentMethod == PaymentMethod.None)
                .ToListAsync();

            var actualIncome = allCash.Where(t => t.Type == SafeTransactionType.Income &&
                !(t.OrderId.HasValue && (t.Description.Contains("تحصيل أجور") || t.Description.Contains("استرداد رأس مال")))
            ).Sum(t => t.Amount);

            var deposits = allCash.Where(t => t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);
            decimal currentBalance = actualIncome - deposits;

            if (amount > currentBalance)
            {
                TempData["ErrorMessage"] = $"الرصيد الفعلي المتوفر في الخزنة الكاش ({currentBalance:N2} ريال) لا يكفي للتوريد.";
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