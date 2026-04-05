using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Threading.Tasks;

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

        // 📌 1. التقرير المالي المتقدم (اللوحة الشاملة ERP Dashboard)
        public async Task<IActionResult> Index(string monthYear)
        {
            DateTime targetDate = string.IsNullOrEmpty(monthYear) ? DateTime.Now : DateTime.Parse(monthYear + "-01");
            ViewBag.CurrentMonthYear = targetDate.ToString("yyyy-MM");
            ViewBag.MonthName = targetDate.ToString("MMMM yyyy");

            // =================================================================
            // 1. قسم الأرباح والخسائر (P&L) - هل أنا كسبان أم خسران؟
            // =================================================================

            // أ. الإيرادات الكلية من الطلبات المدفوعة
            var monthlyOrders = await _context.Orders
                .Include(o => o.UsedSpareParts).ThenInclude(p => p.SparePart)
                .Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month)
                .ToListAsync();

            decimal totalRevenue = monthlyOrders.Sum(o => o.FinalPrice);

            // ب. تكلفة البضاعة المباعة (القطع التي تم استهلاكها في الفواتير فقط)
            decimal cogs = monthlyOrders.SelectMany(o => o.UsedSpareParts).Sum(p => p.QuantityUsed * (p.SparePart?.PurchasePrice ?? 0));

            // ج. إجمالي الربح المبدئي (الإيرادات - تكلفة القطع)
            decimal grossProfit = totalRevenue - cogs;

            // د. المصروفات التشغيلية الحقيقية (إيجار، رواتب، توالف، بنزين)
            var opExpensesList = await _context.Expenses
                .Where(e => e.Date.Year == targetDate.Year && e.Date.Month == targetDate.Month && e.DeductionFrom == DeductionSource.Company)
                .ToListAsync();
            decimal totalOpExpenses = opExpensesList.Sum(e => e.Amount);

            decimal damagesCost = await _context.DamagedParts
                .Where(d => d.Date.Year == targetDate.Year && d.Date.Month == targetDate.Month)
                .SumAsync(d => d.TotalLoss);

            // هـ. عمولات الفنيين التي تم صرفها (تعتبر مصروف تشغيلي يقلل الربح)
            decimal techCommissionsPaid = await _context.SafeTransactions
                .Where(t => t.Date.Year == targetDate.Year && t.Date.Month == targetDate.Month
                         && t.Type == SafeTransactionType.DepositToBank
                         && t.Description.Contains("صرف صافي عمولة الفني"))
                .SumAsync(t => t.Amount);

            decimal allExpenses = totalOpExpenses + damagesCost + techCommissionsPaid;

            // و. صافي الربح الحقيقي للشركة
            decimal netProfit = grossProfit - allExpenses;


            // =================================================================
            // 2. قسم التدفقات النقدية (Cash Flow) - أين ذهبت السيولة؟
            // =================================================================

            // حساب قيمة البضائع المشتراة (للمخزن + نواقص الفنيين)
            var purchasesTransactions = await _context.SafeTransactions
                .Where(t => t.Date.Year == targetDate.Year && t.Date.Month == targetDate.Month
                         && t.Type == SafeTransactionType.DepositToBank
                         && (t.TargetSafe == SafeType.Purchasing || t.Description.Contains("سلفة نقدية من العام لشراء")))
                .ToListAsync();

            decimal totalPurchasesOutflow = purchasesTransactions.Sum(t => t.Amount);

            // إجمالي الأموال الخارجة (مصاريف + عمولات + مشتريات بضاعة)
            decimal totalCashOutflow = allExpenses + totalPurchasesOutflow;


            // =================================================================
            // 3. قسم الأصول والخزنات (Assets & Funds) - كم أمتلك الآن؟
            // =================================================================

            // أ. قيمة المخزن الفعلي (باستثناء القطع المحجوزة للعملاء)
            decimal rawInventoryValue = await _context.SpareParts.SumAsync(p => p.MainStockQuantity * p.PurchasePrice);
            decimal reservedInOrders = await _context.UsedSpareParts.Include(u => u.Order).Include(u => u.SparePart).Where(u => u.Order != null && !u.Order.IsPaid).SumAsync(u => u.QuantityUsed * (u.SparePart != null ? u.SparePart.PurchasePrice : 0));
            decimal reservedMissingParts = await _context.OrderPartRequests.Include(pr => pr.SparePart).Include(pr => pr.Order).Where(pr => pr.RequestType == PartRequestType.PurchaseNew && pr.Status == PartRequestStatus.ReadyForInstallation && pr.Order != null && !pr.Order.IsPaid).SumAsync(pr => pr.Quantity * (pr.SparePart != null ? pr.SparePart.PurchasePrice : 0));
            decimal inventoryValue = rawInventoryValue - reservedInOrders - reservedMissingParts;
            if (inventoryValue < 0) inventoryValue = 0;

            // ب. الخزنات النقدية
            var cashTransactions = await _context.SafeTransactions.Where(s => s.PaymentMethod == PaymentMethod.Cash || s.PaymentMethod == PaymentMethod.None).ToListAsync();
            decimal purchasingSafeBalance = cashTransactions.Where(t => t.TargetSafe == SafeType.Purchasing && t.Type == SafeTransactionType.Income).Sum(t => t.Amount) - cashTransactions.Where(t => t.TargetSafe == SafeType.Purchasing && t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);
            decimal generalSafeBalance = cashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.Income).Sum(t => t.Amount) - cashTransactions.Where(t => t.TargetSafe == SafeType.General && t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);

            // ج. البنك
            var bankTransactions = await _context.SafeTransactions.Where(s => s.PaymentMethod == PaymentMethod.POS || s.PaymentMethod == PaymentMethod.BankTransfer).ToListAsync();
            decimal bankBalance = bankTransactions.Where(t => t.Type == SafeTransactionType.Income).Sum(t => t.Amount) - bankTransactions.Where(t => t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);

            // د. ديون الفنيين (العهد النقدية التي في جيوبهم ولم تورد)
            var techsWalletData = await _context.Technicians
                .Select(t => new {
                    TotalCashOrders = t.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice),
                    TotalIncome = t.TotalIncome
                }).ToListAsync();

            decimal totalCashWithTechs = techsWalletData.Sum(t => t.TotalCashOrders - t.TotalIncome);


            // =================================================================
            // 4. إرسال البيانات للواجهة
            // =================================================================
            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.COGS = cogs;
            ViewBag.GrossProfit = grossProfit;
            ViewBag.AllExpenses = allExpenses;
            ViewBag.NetProfit = netProfit;

            ViewBag.TotalPurchasesOutflow = totalPurchasesOutflow;
            ViewBag.TotalCashOutflow = totalCashOutflow;

            ViewBag.InventoryValue = inventoryValue;
            ViewBag.PurchasingSafeBalance = purchasingSafeBalance;
            ViewBag.GeneralSafeBalance = generalSafeBalance;
            ViewBag.BankBalance = bankBalance;
            ViewBag.TotalCashWithTechs = totalCashWithTechs > 0 ? totalCashWithTechs : 0;

            // الشركاء
            var partnersList = await _context.Partners.ToListAsync();
            ViewBag.TotalPartnersShare = partnersList.Sum(p => p.SharePercentage);
            ViewBag.Partners = partnersList.Select(p => new { Id = p.Id, Name = p.Name, Share = p.SharePercentage, Amount = netProfit > 0 ? (netProfit * (p.SharePercentage / 100m)) : 0 }).ToList();

            // الفنيين
            var technicians = await _context.Technicians.Include(t => t.AssignedOrders).ThenInclude(o => o.UsedSpareParts).ThenInclude(p => p.SparePart).Include(t => t.Expenses).ToListAsync();
            ViewBag.TechnicianReports = technicians.Select(t => {
                var currentMonthOrders = t.AssignedOrders.Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month).ToList();
                decimal wallet = t.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice) - t.TotalIncome;
                dynamic expando = new ExpandoObject();
                expando.Id = t.TechnicianId;
                expando.Name = t.Name;
                expando.TotalSales = currentMonthOrders.Sum(o => o.FinalPrice);
                expando.PartsCost = currentMonthOrders.SelectMany(o => o.UsedSpareParts).Sum(p => p.QuantityUsed * (p.SparePart?.PurchasePrice ?? 0));
                expando.AllTimeDeductions = t.Expenses?.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount) ?? 0;
                expando.WalletBalance = wallet < 0 ? 0 : wallet;
                return expando;
            }).ToList();

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> AdjustSafeBalance(SafeType targetSafe, decimal actualBalance, string notes)
        {
            var transactions = await _context.SafeTransactions.Where(s => s.TargetSafe == targetSafe && (s.PaymentMethod == PaymentMethod.Cash || s.PaymentMethod == PaymentMethod.None)).ToListAsync();
            decimal currentBalance = transactions.Where(t => t.Type == SafeTransactionType.Income).Sum(t => t.Amount)
                                   - transactions.Where(t => t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);
            decimal difference = actualBalance - currentBalance;

            if (difference != 0)
            {
                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = Math.Abs(difference),
                    Type = difference > 0 ? SafeTransactionType.Income : SafeTransactionType.DepositToBank,
                    TargetSafe = targetSafe,
                    PaymentMethod = PaymentMethod.Cash,
                    Description = $"[تسوية جردية يدوية للدرج]: {notes}",
                    RecordedBy = User.Identity?.Name ?? "System",
                    Date = DateTime.Now
                });
                string safeName = targetSafe == SafeType.General ? "الخزنة العامة" : "خزنة المشتريات";
                LogAction("تسوية خزنة", $"تم تعديل رصيد {safeName} يدوياً ليصبح {actualBalance} ريال.");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"تم تسوية رصيد {safeName} بنجاح ليصبح {actualBalance} ريال.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> AddPartner(string name, decimal sharePercentage)
        {
            decimal currentTotalShares = await _context.Partners.SumAsync(p => p.SharePercentage);
            if (currentTotalShares + sharePercentage > 100)
            {
                TempData["ErrorMessage"] = "مجموع نسب الشركاء يتجاوز 100%."; return RedirectToAction(nameof(Index));
            }
            _context.Partners.Add(new Partner { Name = name, SharePercentage = sharePercentage });
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم الإضافة بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> DeletePartner(int id)
        {
            var partner = await _context.Partners.FindAsync(id);
            if (partner != null)
            {
                _context.Partners.Remove(partner);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم إزالة الشريك بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> SettlePartnerShare(int partnerId, string partnerName, decimal amount, string monthYearName, PaymentMethod paymentMethod)
        {
            if (amount > 0)
            {
                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = amount,
                    Type = SafeTransactionType.DepositToBank,
                    TargetSafe = SafeType.General,
                    PaymentMethod = paymentMethod,
                    Description = $"توزيع وصرف أرباح الشريك ({partnerName})",
                    RecordedBy = User.Identity?.Name ?? "System",
                    Date = DateTime.Now
                });
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"تم صرف أرباح الشريك بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }

        // 📌 إدارة الفواتير
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

                    if (invoice.Order.PaymentMethod == PaymentMethod.Cash)
                    {
                        _context.SafeTransactions.Add(new SafeTransaction
                        {
                            Amount = invoice.Amount,
                            Type = SafeTransactionType.DepositToBank,
                            TargetSafe = SafeType.General,
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

        // 📌 3. شاشة العمولات الذكية
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
                            .ThenInclude(p => p.SparePart)
                    .Include(t => t.Expenses)
                    .FirstOrDefaultAsync(t => t.TechnicianId == techId);

                if (tech != null)
                {
                    var monthOrders = tech.AssignedOrders.Where(o => o.IsPaid && o.CreatedAt.Year == targetDate.Year && o.CreatedAt.Month == targetDate.Month).ToList();

                    decimal totalRev = monthOrders.Sum(o => o.FinalPrice);
                    decimal laborRev = monthOrders.Sum(o => o.IsFeeApplied ? o.EstimatedPrice : 0);
                    decimal partsCost = monthOrders.SelectMany(o => o.UsedSpareParts).Sum(p => p.QuantityUsed * (p.SparePart?.PurchasePrice ?? 0));
                    decimal deductions = tech.Expenses?.Where(e => e.DeductionFrom == DeductionSource.Technician).Sum(e => e.Amount) ?? 0;
                    decimal cashInHand = tech.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice) - tech.TotalIncome;

                    ViewBag.Technician = tech;
                    ViewBag.TotalRevenue = totalRev;
                    ViewBag.LaborRevenue = laborRev;
                    ViewBag.PartsCost = partsCost;
                    ViewBag.TotalDeductions = deductions;
                    ViewBag.CashInHand = cashInHand < 0 ? 0 : cashInHand;
                    ViewBag.TechOrders = monthOrders;
                    ViewBag.DeductionsList = tech.Expenses?.Where(e => e.DeductionFrom == DeductionSource.Technician).ToList() ?? new List<Expense>();
                }
            }
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> PayCommission(int techId, string monthYearName, decimal finalAmount, string settleType, string notes, PaymentMethod paymentMethod)
        {
            var tech = await _context.Technicians
                .Include(t => t.Expenses)
                .Include(t => t.AssignedOrders)
                .FirstOrDefaultAsync(t => t.TechnicianId == techId);

            if (tech != null)
            {
                if (tech.Expenses != null && tech.Expenses.Any())
                {
                    var techExpenses = tech.Expenses.Where(e => e.DeductionFrom == DeductionSource.Technician).ToList();
                    _context.Expenses.RemoveRange(techExpenses);
                }

                var cashOrdersTotal = tech.AssignedOrders.Where(o => o.IsPaid && o.PaymentMethod == PaymentMethod.Cash).Sum(o => o.FinalPrice);
                tech.TotalIncome = cashOrdersTotal;
                _context.Update(tech);

                if (finalAmount > 0)
                {
                    if (settleType == "PayToTech")
                    {
                        _context.SafeTransactions.Add(new SafeTransaction
                        {
                            Amount = finalAmount,
                            Type = SafeTransactionType.DepositToBank,
                            TargetSafe = SafeType.General,
                            PaymentMethod = paymentMethod,
                            Description = $"صرف صافي عمولة الفني ({tech.Name}) عن شهر {monthYearName}. {notes}",
                            RecordedBy = User.Identity?.Name ?? "System",
                            Date = DateTime.Now
                        });
                    }
                    else if (settleType == "ReceiveFromTech")
                    {
                        _context.SafeTransactions.Add(new SafeTransaction
                        {
                            Amount = finalAmount,
                            Type = SafeTransactionType.Income,
                            TargetSafe = SafeType.General,
                            PaymentMethod = paymentMethod,
                            Description = $"استلام وتوريد كاش (بعد التصفية) من الفني ({tech.Name}) عن شهر {monthYearName}. {notes}",
                            RecordedBy = User.Identity?.Name ?? "System",
                            Date = DateTime.Now
                        });
                    }
                }

                LogAction("تصفية حساب فني", $"تم تصفية حساب الفني {tech.Name} بنجاح.");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم التصفية وتوريد/صرف المبالغ بنجاح.";
            }
            return RedirectToAction(nameof(TechnicianCommissions), new { techId = techId });
        }

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

            _context.SafeTransactions.Add(new SafeTransaction
            {
                Amount = expense.Amount,
                Type = SafeTransactionType.DepositToBank,
                TargetSafe = SafeType.General,
                PaymentMethod = expense.PaymentMethod,
                Description = $"مصروف ({expense.PaymentMethod}): {expense.Description}",
                RecordedBy = expense.RecordedBy,
                Date = DateTime.Now
            });

            await _context.SaveChangesAsync();
            string msg = expense.PaymentMethod == PaymentMethod.Cash ? "وخصمه من الدرج (الكاش)" : "وتسجيله كخصم بنكي";
            TempData["SuccessMessage"] = $"تم تسجيل المصروف {msg} بنجاح.";
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