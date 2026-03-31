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

        // 📌 دالة مساعدة لتسجيل اللوج بأمان (مع تسجيل الصلاحية)
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

            decimal totalIncome = transactions.Where(t => t.Type == SafeTransactionType.Income).Sum(t => t.Amount);
            decimal totalDeposits = transactions.Where(t => t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);

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

            var income = await _context.SafeTransactions.Where(t => t.Type == SafeTransactionType.Income).SumAsync(t => t.Amount);
            var deposits = await _context.SafeTransactions.Where(t => t.Type == SafeTransactionType.DepositToBank).SumAsync(t => t.Amount);
            decimal currentBalance = income - deposits;

            if (amount > currentBalance)
            {
                TempData["ErrorMessage"] = $"الرصيد المتوفر في الخزنة ({currentBalance} ريال) لا يكفي للتوريد.";
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

            // 📌 تسجيل الحركة
            LogAction("توريد أموال من الخزنة", $"تم توريد مبلغ {amount} ريال من الخزنة إلى البنك. البيان: {description}");

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم توريد الأموال وخصمها من الخزنة بنجاح.";
            return RedirectToAction(nameof(Index));
        }
    }
}