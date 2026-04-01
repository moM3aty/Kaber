// مسار الملف: Controllers/AccountingController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using System.Collections.Generic;
using System.Dynamic;

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
            _context.SystemLogs.Add(new SystemLog
            {
                ActionType = actionType,
                Details = details,
                Username = username,
                Timestamp = DateTime.Now
            });
        }

        // 📌 1. تقرير الأرباح (قائمة الدخل)
        public async Task<IActionResult> Index(string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");
            ViewBag.CurrentMonthYear = targetDate.ToString("yyyy-MM");
            ViewBag.MonthName = targetDate.ToString("MMMM yyyy");

            // جلب الطلبات المدفوعة في هذا الشهر
            var monthlyOrders = await _context.Orders
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(p => p.SparePart)
                .Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month)
                .ToListAsync();

            // 1. الإيرادات (إجمالي الفواتير)
            decimal totalRevenue = monthlyOrders.Sum(o => o.FinalPrice);

            // 2. تكلفة البضاعة المباعة (سعر الشراء للقطع المستهلكة)
            decimal cogs = monthlyOrders.SelectMany(o => o.UsedSpareParts)
                .Sum(p => p.QuantityUsed * (p.SparePart?.PurchasePrice ?? 0));

            // 3. مجمل الربح
            decimal grossProfit = totalRevenue - cogs;

            // 4. المصروفات الخاصة بالشركة هذا الشهر (تستثنى مديونيات الفنيين)
            decimal companyExpenses = await _context.Expenses
                .Where(e => e.Date.Year == targetDate.Year && e.Date.Month == targetDate.Month && e.DeductionFrom == DeductionSource.Company)
                .SumAsync(e => e.Amount);

            // 5. التوالف هذا الشهر
            decimal damagesCost = await _context.DamagedParts
                .Where(d => d.Date.Year == targetDate.Year && d.Date.Month == targetDate.Month)
                .SumAsync(d => d.TotalLoss);

            // 6. صافي الربح
            decimal netProfit = grossProfit - companyExpenses - damagesCost;

            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.CostOfGoodsSold = cogs;
            ViewBag.GrossProfit = grossProfit;
            ViewBag.TotalExpenses = companyExpenses;
            ViewBag.TotalDamagesCost = damagesCost;
            ViewBag.NetCompanyProfit = netProfit;

            // 📌 إحصائيات الفنيين
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                    .ThenInclude(o => o.UsedSpareParts)
                .Include(t => t.Expenses)
                .ToListAsync();

            var techReports = technicians.Select(t => {
                var currentMonthOrders = t.AssignedOrders.Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month).ToList();

                decimal totalSales = currentMonthOrders.Sum(o => o.FinalPrice);
                decimal partsSales = currentMonthOrders.SelectMany(o => o.UsedSpareParts).Sum(p => p.QuantityUsed * p.SellingPriceAtTime);
                decimal laborSales = totalSales - partsSales; // أجور اليد

                // حساب الكاش المحصل بيده (كل الطلبات الكاش التي لم تورد)
                decimal wallet = t.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice) - t.TotalIncome; // TotalIncome تستخدم هنا كتسديد عهدة مجازاً

                // حساب السلف والخصومات
                decimal allTimeDeductions = t.Expenses?.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount) ?? 0;

                dynamic expando = new ExpandoObject();
                expando.Id = t.TechnicianId;
                expando.Name = t.Name;
                expando.TotalIncome = totalSales;
                expando.LaborSales = laborSales;
                expando.PartsSales = partsSales;
                expando.AllTimeDeductions = allTimeDeductions;
                expando.WalletBalance = wallet < 0 ? 0 : wallet;

                return expando;
            }).ToList();

            ViewBag.TechnicianReports = techReports;

            return View();
        }

        // 📌 2. إدارة الفواتير
        public async Task<IActionResult> Invoices(string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");
            ViewBag.CurrentMonthYear = targetDate.ToString("yyyy-MM");

            var invoices = await _context.Invoices
                .Include(i => i.Order)
                    .ThenInclude(o => o.Technician)
                .Where(i => i.IssuedAt.Year == targetDate.Year && i.IssuedAt.Month == targetDate.Month)
                .OrderByDescending(i => i.IssuedAt)
                .ToListAsync();

            return View(invoices);
        }

        [HttpPost]
        public async Task<IActionResult> UpdateInvoiceStatus(int invoiceId, InvoiceStatus newStatus)
        {
            var invoice = await _context.Invoices.FindAsync(invoiceId);
            if (invoice != null)
            {
                invoice.Status = newStatus;
                _context.Update(invoice);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تحديث حالة الفاتورة بنجاح.";
            }
            return RedirectToAction(nameof(Invoices));
        }

        [HttpPost]
        public async Task<IActionResult> RefundInvoice(int invoiceId, string refundReason)
        {
            var invoice = await _context.Invoices.Include(i => i.Order).FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);
            if (invoice != null && invoice.Status == InvoiceStatus.Paid)
            {
                invoice.Status = InvoiceStatus.Rejected;

                if (invoice.Order != null)
                {
                    invoice.Order.IsPaid = false;
                    _context.Update(invoice.Order);

                    // عكس القيد في الخزنة
                    if (invoice.Order.PaymentMethod == PaymentMethod.Cash)
                    {
                        _context.SafeTransactions.Add(new SafeTransaction
                        {
                            Amount = invoice.Amount,
                            Type = SafeTransactionType.DepositToBank, // نستخدم هذا النوع كمسحوبات/مستردات
                            Description = $"استرداد فاتورة ملغاة #{invoiceId}. السبب: {refundReason}",
                            RecordedBy = User.Identity?.Name ?? "System",
                            Date = DateTime.Now
                        });
                    }
                }

                _context.Update(invoice);
                LogAction("استرداد فاتورة", $"تم إلغاء واسترداد الفاتورة #{invoiceId} بقيمة {invoice.Amount}. السبب: {refundReason}");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم استرداد الفاتورة وخصمها من الخزنة بنجاح.";
            }
            return RedirectToAction(nameof(Invoices));
        }

        // 📌 3. شاشة العمولات (مربوطة مع الـ JS في View)
        public async Task<IActionResult> TechnicianCommissions(int? techId, string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");
            ViewBag.CurrentMonthYear = targetDate.ToString("yyyy-MM");
            ViewBag.MonthYearName = targetDate.ToString("MMMM yyyy");
            ViewData["Technicians"] = await _context.Technicians.ToListAsync();

            if (techId.HasValue)
            {
                var tech = await _context.Technicians
                    .Include(t => t.AssignedOrders)
                        .ThenInclude(o => o.UsedSpareParts)
                    .Include(t => t.Expenses)
                    .FirstOrDefaultAsync(t => t.TechnicianId == techId);

                if (tech != null)
                {
                    var monthOrders = tech.AssignedOrders.Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month).ToList();

                    decimal totalRev = monthOrders.Sum(o => o.FinalPrice);
                    decimal partsRev = monthOrders.SelectMany(o => o.UsedSpareParts).Sum(p => p.QuantityUsed * p.SellingPriceAtTime);
                    decimal laborRev = totalRev - partsRev;
                    decimal deductions = tech.Expenses?.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount) ?? 0;

                    decimal cashInHand = tech.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice) - tech.TotalIncome;

                    ViewBag.Technician = tech;
                    ViewBag.TotalRevenue = totalRev;
                    ViewBag.LaborRevenue = laborRev;
                    ViewBag.PartsRevenue = partsRev;
                    ViewBag.TotalDeductions = deductions;
                    ViewBag.CashInHand = cashInHand < 0 ? 0 : cashInHand;
                    ViewBag.TechOrders = monthOrders;
                    ViewBag.DeductionsList = tech.Expenses?.Where(e => e.DeductionFrom == DeductionSource.Technician).ToList() ?? new List<Expense>();
                }
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> PayCommission(int techId, string monthYearName, decimal deductionAmount, decimal finalAmount, string notes)
        {
            var tech = await _context.Technicians.Include(t => t.Expenses).FirstOrDefaultAsync(t => t.TechnicianId == techId);
            if (tech != null)
            {
                // تصفية السلف (حذفها لأنها خُصمت)
                if (tech.Expenses != null && tech.Expenses.Any())
                {
                    var techExpenses = tech.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician).ToList();
                    _context.Expenses.RemoveRange(techExpenses);
                }

                // تسجيل منصرف الخزنة (الراتب/العمولة)
                if (finalAmount > 0)
                {
                    _context.SafeTransactions.Add(new SafeTransaction
                    {
                        Amount = finalAmount,
                        Type = SafeTransactionType.DepositToBank, // كمصروف/سحب من الخزنة
                        Description = $"صرف عمولة الفني ({tech.Name}) عن شهر {monthYearName}. {notes}",
                        RecordedBy = User.Identity?.Name ?? "System",
                        Date = DateTime.Now
                    });
                }

                LogAction("صرف عمولات", $"تم تصفية وصرف مستحقات الفني {tech.Name} بصافي {finalAmount} ريال.");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تصفية مستحقات الفني وتصفير السلف بنجاح.";
            }
            return RedirectToAction(nameof(TechnicianCommissions), new { techId = techId });
        }

        [HttpPost]
        public async Task<IActionResult> SettleTechCash(int techId, decimal amount, string note)
        {
            var tech = await _context.Technicians.FindAsync(techId);
            if (tech != null)
            {
                tech.TotalIncome += amount; // هذا الحقل يُستخدم كـ (مجموع الكاش المُورد من الفني للشركة)
                _context.Update(tech);

                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = amount,
                    Type = SafeTransactionType.Income,
                    Description = $"توريد كاش من الفني ({tech.Name}). {note}",
                    RecordedBy = User.Identity?.Name ?? "System",
                    Date = DateTime.Now
                });

                LogAction("توريد كاش فني", $"قام الفني {tech.Name} بتوريد كاش للخزنة بقيمة {amount} ريال.");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم استلام الكاش من الفني وإيداعه بالخزنة بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }

        // 📌 4. دفتر الأستاذ (Master Ledger)
        public async Task<IActionResult> MasterLedger(string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");
            ViewBag.CurrentMonthYear = targetDate.ToString("yyyy-MM");
            ViewBag.MonthName = targetDate.ToString("MMMM yyyy");

            var orders = await _context.Orders
                .Include(o => o.Technician)
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(p => p.SparePart)
                .Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();

            return View(orders);
        }

        // 📌 5. المصروفات (تم دمجها هنا لمركزية العمليات)
        public async Task<IActionResult> Expenses(string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");
            ViewBag.CurrentMonthYear = targetDate.ToString("yyyy-MM");

            var expenses = await _context.Expenses
               .Include(e => e.Technician)
               .Where(e => e.Date.Year == targetDate.Year && e.Date.Month == targetDate.Month)
               .OrderByDescending(e => e.Date)
               .ToListAsync();

            ViewData["TechnicianId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(await _context.Technicians.ToListAsync(), "TechnicianId", "Name");
            return View(expenses);
        }

        [HttpPost]
        public async Task<IActionResult> AddExpense(Expense expense)
        {
            expense.Date = DateTime.Now;
            expense.RecordedBy = User.Identity?.Name ?? "System";
            _context.Expenses.Add(expense);

            // سحب من الخزنة
            _context.SafeTransactions.Add(new SafeTransaction
            {
                Amount = expense.Amount,
                Type = SafeTransactionType.DepositToBank,
                Description = $"مصروف: {expense.Description}",
                RecordedBy = expense.RecordedBy,
                Date = DateTime.Now
            });

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم تسجيل المصروف وخصمه من الخزنة.";
            return RedirectToAction(nameof(Expenses));
        }

        [HttpPost]
        public async Task<IActionResult> DeleteExpense(int id)
        {
            var expense = await _context.Expenses.FindAsync(id);
            if (expense != null)
            {
                _context.Expenses.Remove(expense);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم إلغاء المصروف.";
            }
            return RedirectToAction(nameof(Expenses));
        }
    }
}