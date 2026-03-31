using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,Accounting")] // متاح للأدمن والمحاسبة فقط
    public class SafeController : Controller
    {
        private readonly ApplicationDbContext _context;

        public SafeController(ApplicationDbContext context)
        {
            _context = context;
        }

        // 📌 عرض الرصيد والحركات
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

        // 📌 توريد أموال (سحب من الخزنة للبنك/المالك)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DepositMoney(decimal amount, string description)
        {
            if (amount <= 0)
            {
                TempData["ErrorMessage"] = "يجب أن يكون المبلغ أكبر من صفر.";
                return RedirectToAction(nameof(Index));
            }

            // التأكد من وجود رصيد كافي في الخزنة
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

            // تسجيل في الـ Logs
            _context.SystemLogs.Add(new SystemLog { ActionType = "توريد أموال", Details = $"تم توريد مبلغ {amount} ريال من الخزنة. البيان: {description}", Username = User.Identity?.Name, Timestamp = DateTime.Now });

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم توريد الأموال وخصمها من الخزنة بنجاح.";
            return RedirectToAction(nameof(Index));
        }
    }
}