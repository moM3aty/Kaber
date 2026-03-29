using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,Accounting")]
    public class ExpensesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ExpensesController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            // جلب المصروفات مع بيانات الفني إن وجدت
            var expenses = await _context.Expenses
                .Include(e => e.Technician)
                .OrderByDescending(e => e.Date)
                .ToListAsync();

            ViewData["TotalExpenses"] = expenses.Sum(e => e.Amount);

            // 📌 إرسال قائمة الفنيين لصفحة المصروفات لاختيار أحدهم
            ViewData["Technicians"] = new SelectList(await _context.Technicians.ToListAsync(), "TechnicianId", "Name");

            return View(expenses);
        }

        [HttpPost]
        public async Task<IActionResult> Create(string description, decimal amount, int? technicianId)
        {
            var expense = new Expense
            {
                Description = description,
                Amount = amount,
                TechnicianId = technicianId, // إذا كان Null فهو مصروف شركة عام
                Date = DateTime.Now,
                RecordedBy = User.Identity?.Name ?? "Admin"
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = technicianId.HasValue
                ? "تم تسجيل المصروف/السلفة وسيتم خصمه من حساب الفني."
                : "تم تسجيل المصروف العام للشركة بنجاح.";

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