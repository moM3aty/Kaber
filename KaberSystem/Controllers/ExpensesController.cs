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
    public class ExpensesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ExpensesController(ApplicationDbContext context) { _context = context; }

        public async Task<IActionResult> Index()
        {
            var expenses = await _context.Expenses.OrderByDescending(e => e.Date).ToListAsync();
            ViewData["TotalExpenses"] = expenses.Sum(e => e.Amount);
            return View(expenses);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string description, decimal amount)
        {
            if (!string.IsNullOrEmpty(description) && amount > 0)
            {
                var expense = new Expense
                {
                    Description = description,
                    Amount = amount,
                    Date = DateTime.Now,
                    RecordedBy = User.Identity.Name ?? "المحاسب"
                };
                _context.Expenses.Add(expense);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تسجيل المصروف بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense != null)
            {
                _context.Expenses.Remove(expense);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف المصروف بنجاح وتعديل الإجماليات.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}