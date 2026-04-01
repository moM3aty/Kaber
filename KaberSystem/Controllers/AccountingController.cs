using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

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

        // 📌 التحديث الجذري: الفلترة الشهرية لقائمة الدخل والحسابات
        public async Task<IActionResult> Index(string monthYear)
        {
            // تحديد الشهر المراد عرضه (الافتراضي هو الشهر الحالي)
            DateTime filterDate = DateTime.Now;
            if (!string.IsNullOrEmpty(monthYear) && DateTime.TryParse(monthYear + "-01", out DateTime parsed))
            {
                filterDate = parsed;
            }
            else
            {
                monthYear = filterDate.ToString("yyyy-MM");
            }

            int month = filterDate.Month;
            int year = filterDate.Year;

            // 1. جلب الطلبات المدفوعة في الشهر المحدد فقط
            var paidOrders = await _context.Orders
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(up => up.SparePart)
                .Where(o => o.IsPaid && o.CreatedAt.Month == month && o.CreatedAt.Year == year)
                .ToListAsync();

            // 2. إجمالي الإيرادات (المبيعات) للشهر المحدد
            decimal totalRevenue = paidOrders.Sum(o => o.FinalPrice);

            // 3. حساب (تكلفة البضاعة المباعة - COGS) للشهر المحدد
            decimal costOfGoodsSold = paidOrders.SelectMany(o => o.UsedSpareParts)
                .Sum(up => up.QuantityUsed * (up.SparePart?.PurchasePrice ?? 0));

            // 4. مجمل الربح للشهر المحدد
            decimal grossProfit = totalRevenue - costOfGoodsSold;

            // 5. المصروفات التشغيلية للشركة في الشهر المحدد
            decimal companyExpenses = await _context.Expenses
                .Where(e => e.DeductionFrom == DeductionSource.Company && e.Date.Month == month && e.Date.Year == year)
                .SumAsync(e => e.Amount);

            // 6. خسائر التوالف في الشهر المحدد
            decimal totalDamagesCost = await _context.DamagedParts
                .Where(d => d.Date.Month == month && d.Date.Year == year)
                .SumAsync(d => d.TotalLoss);

            // 7. صافي الربح النهائي للشركة في الشهر المحدد
            decimal netCompanyProfit = grossProfit - companyExpenses - totalDamagesCost;

            // حسابات الفنيين (بعضها شهري والبعض الآخر تراكمي للترحيل)
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Expenses)
                .ToListAsync();

            var techReports = technicians.Select(t => new {
                Id = t.TechnicianId,
                Name = t.Name,
                // الإيرادات التي حققها في هذا الشهر فقط
                GrossIncome = t.AssignedOrders.Where(o => o.IsPaid && o.CreatedAt.Month == month && o.CreatedAt.Year == year).Sum(o => o.FinalPrice),
                // السلف والخصومات التي تمت في هذا الشهر فقط
                TotalDeductions = t.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician && e.Date.Month == month && e.Date.Year == year).Sum(e => e.Amount),
                // 📌 الأرصدة المرحلة: الكاش الموجود في يده (تراكمي حتى يتم توريده)
                WalletBalance = t.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice),
                // 📌 الأرصدة المرحلة: إجمالي المديونيات المتراكمة عليه (كل الشهور) للفت الانتباه
                AllTimeDeductions = t.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount)
            }).ToList();

            // إرسال البيانات للواجهة
            ViewBag.CurrentMonthYear = monthYear; // ليتم وضعه في حقل اختيار الشهر في الواجهة
            ViewBag.MonthName = filterDate.ToString("MMMM yyyy");

            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.CostOfGoodsSold = costOfGoodsSold;
            ViewBag.GrossProfit = grossProfit;
            ViewBag.TotalExpenses = companyExpenses;
            ViewBag.TotalDamagesCost = totalDamagesCost;
            ViewBag.NetCompanyProfit = netCompanyProfit;

            ViewBag.TechnicianReports = techReports;

            return View();
        }

        public async Task<IActionResult> TechnicianCommissions(int? techId, string monthYear)
        {
            ViewData["Technicians"] = await _context.Technicians.ToListAsync();

            if (!techId.HasValue || string.IsNullOrEmpty(monthYear))
            {
                return View(null);
            }

            var parsedDate = DateTime.Parse(monthYear + "-01");
            int selectedMonth = parsedDate.Month;
            int selectedYear = parsedDate.Year;

            var technician = await _context.Technicians.FindAsync(techId);

            var techOrders = await _context.Orders
                .Include(o => o.UsedSpareParts)
                .Where(o => o.TechnicianId == techId &&
                            o.IsPaid &&
                            (o.Status == OrderStatus.Completed || o.Status == OrderStatus.Approved) &&
                            o.CreatedAt.Month == selectedMonth &&
                            o.CreatedAt.Year == selectedYear)
                .ToListAsync();

            var deductions = await _context.Expenses
                .Where(e => e.TechnicianId == techId &&
                            e.DeductionFrom == DeductionSource.Technician &&
                            e.Date.Month == selectedMonth &&
                            e.Date.Year == selectedYear)
                .ToListAsync();

            decimal totalRevenue = techOrders.Sum(o => o.FinalPrice);
            decimal laborRevenue = techOrders.Where(o => o.IsFeeApplied).Sum(o => o.EstimatedPrice);
            decimal partsRevenue = totalRevenue - laborRevenue;
            decimal totalDeductions = deductions.Sum(e => e.Amount);
            decimal cashInHand = techOrders.Where(o => o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice);

            ViewBag.Technician = technician;
            ViewBag.MonthYearName = parsedDate.ToString("MMMM yyyy");
            ViewBag.TechOrders = techOrders;
            ViewBag.DeductionsList = deductions;

            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.LaborRevenue = laborRevenue;
            ViewBag.PartsRevenue = partsRevenue;
            ViewBag.TotalDeductions = totalDeductions;
            ViewBag.CashInHand = cashInHand;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PayCommission(int techId, string monthYearName, decimal finalAmount, decimal deductionAmount, string notes)
        {
            var tech = await _context.Technicians.FindAsync(techId);
            if (tech != null && finalAmount > 0)
            {
                _context.Expenses.Add(new Expense
                {
                    Description = $"صرف عمولة/راتب الفني ({tech.Name}) عن شهر {monthYearName}. {notes}",
                    Amount = finalAmount,
                    DeductionFrom = DeductionSource.Company,
                    TechnicianId = techId,
                    Date = DateTime.Now,
                    RecordedBy = User.Identity?.Name ?? "Accounting"
                });

                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = finalAmount,
                    Type = SafeTransactionType.DepositToBank,
                    Description = $"صرف عمولة الفني ({tech.Name}) - شهر {monthYearName}",
                    Date = DateTime.Now,
                    RecordedBy = User.Identity?.Name ?? "Accounting"
                });

                LogAction("صرف عمولة فني", $"تم صرف عمولة للفني {tech.Name} بقيمة {finalAmount} ريال عن شهر {monthYearName}.");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"تم تسجيل صرف العمولة ({finalAmount} ريال) للفني بنجاح وخصمها من الخزنة.";
            }
            return RedirectToAction(nameof(Index));
        }

        // 📌 التحديث: عرض فواتير الشهر المحدد فقط لتسهيل المراجعة
        public async Task<IActionResult> Invoices(string monthYear)
        {
            DateTime filterDate = DateTime.Now;
            if (!string.IsNullOrEmpty(monthYear) && DateTime.TryParse(monthYear + "-01", out DateTime parsed))
            {
                filterDate = parsed;
            }
            else
            {
                monthYear = filterDate.ToString("yyyy-MM");
            }

            ViewBag.CurrentMonthYear = monthYear;

            var invoices = await _context.Invoices
                .Include(i => i.Order)
                    .ThenInclude(o => o.Technician)
                .Where(i => i.IssuedAt.Month == filterDate.Month && i.IssuedAt.Year == filterDate.Year)
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RefundInvoice(int invoiceId, string refundReason)
        {
            var invoice = await _context.Invoices.Include(i => i.Order).FirstOrDefaultAsync(i => i.InvoiceId == invoiceId);

            if (invoice != null && invoice.Status == InvoiceStatus.Paid)
            {
                invoice.Status = InvoiceStatus.Rejected;
                _context.Update(invoice);

                if (invoice.Order != null)
                {
                    invoice.Order.IsPaid = false;
                    invoice.Order.PaymentMethod = PaymentMethod.None;
                    _context.Update(invoice.Order);
                }

                var originalSafeTransaction = await _context.SafeTransactions
                    .FirstOrDefaultAsync(s => s.OrderId == invoice.OrderId && s.Type == SafeTransactionType.Income);

                if (originalSafeTransaction != null)
                {
                    var refundTransaction = new SafeTransaction
                    {
                        Amount = originalSafeTransaction.Amount,
                        Type = SafeTransactionType.DepositToBank,
                        Description = $"استرداد مالي (إلغاء فاتورة #{invoiceId}). السبب: {refundReason}",
                        OrderId = invoice.OrderId,
                        Date = DateTime.Now,
                        RecordedBy = User.Identity?.Name ?? "Accounting"
                    };
                    _context.SafeTransactions.Add(refundTransaction);
                }

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

        // 📌 التحديث: عرض مصروفات الشهر المحدد فقط
        public async Task<IActionResult> Expenses(string monthYear)
        {
            DateTime filterDate = DateTime.Now;
            if (!string.IsNullOrEmpty(monthYear) && DateTime.TryParse(monthYear + "-01", out DateTime parsed))
            {
                filterDate = parsed;
            }
            else
            {
                monthYear = filterDate.ToString("yyyy-MM");
            }

            ViewBag.CurrentMonthYear = monthYear;
            ViewData["TechnicianId"] = new SelectList(await _context.Technicians.ToListAsync(), "TechnicianId", "Name");

            var expenses = await _context.Expenses
                .Include(e => e.Technician)
                .Where(e => e.Date.Month == filterDate.Month && e.Date.Year == filterDate.Year)
                .OrderByDescending(e => e.Date)
                .ToListAsync();

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