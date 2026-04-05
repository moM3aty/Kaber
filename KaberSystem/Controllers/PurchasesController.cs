// مسار الملف: Controllers/PurchasesController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Collections.Generic;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,PurchasingManager,PurchasingRep,Store")]
    public class PurchasesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PurchasesController(ApplicationDbContext context)
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

        // 📌 التحديث الجذري: دالة خصم قيمة الشراء وتطبيق لوجيك "تمويل العجز" من الخزنة العامة
        private async Task DeductPurchaseCostAsync(decimal totalCost, string itemName, PaymentMethod paymentMethod)
        {
            // نحسب رصيد خزنة المشتريات الحالي للطريقة اللي الدفع بيها (لو كاش نشوف كاش المشتريات، لو شبكة نشوف بنك المشتريات)
            var purchasingTransactions = await _context.SafeTransactions
                .Where(s => s.TargetSafe == SafeType.Purchasing && s.PaymentMethod == paymentMethod)
                .ToListAsync();

            decimal purchasingBalance = purchasingTransactions.Where(t => t.Type == SafeTransactionType.Income).Sum(t => t.Amount)
                                      - purchasingTransactions.Where(t => t.Type == SafeTransactionType.DepositToBank).Sum(t => t.Amount);

            // 1. لو رصيد المشتريات يغطي التكلفة بالكامل -> يتم الخصم منه فوراً
            if (purchasingBalance >= totalCost)
            {
                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = totalCost,
                    Type = SafeTransactionType.DepositToBank,
                    TargetSafe = SafeType.Purchasing,
                    PaymentMethod = paymentMethod,
                    Description = $"شراء قطع غيار: {itemName}",
                    RecordedBy = User.Identity?.Name ?? "System",
                    Date = DateTime.Now
                });
            }
            else
            {
                // 2. لو رصيد المشتريات لا يكفي -> نسحب كل المتاح فيه، وناخد الباقي "سلفة" من الأرباح العامة
                decimal deficit = totalCost - purchasingBalance;

                // أ. سحب وتصفير ما تبقى في خزنة المشتريات (إن وجد)
                if (purchasingBalance > 0)
                {
                    _context.SafeTransactions.Add(new SafeTransaction
                    {
                        Amount = purchasingBalance,
                        Type = SafeTransactionType.DepositToBank,
                        TargetSafe = SafeType.Purchasing,
                        PaymentMethod = paymentMethod,
                        Description = $"سحب كامل الرصيد لتغطية جزء من شراء: {itemName}",
                        RecordedBy = User.Identity?.Name ?? "System",
                        Date = DateTime.Now
                    });
                }

                // ب. سحب العجز من الخزنة العامة كتمويل
                _context.SafeTransactions.Add(new SafeTransaction
                {
                    Amount = deficit,
                    Type = SafeTransactionType.DepositToBank,
                    TargetSafe = SafeType.General, // 🌟 العجز يُخصم من أرباح المؤسسة كتمويل
                    PaymentMethod = paymentMethod,
                    Description = $"سلفة لتمويل عجز المشتريات لشراء: {itemName}",
                    RecordedBy = User.Identity?.Name ?? "System",
                    Date = DateTime.Now
                });

                LogAction("تمويل عجز مشتريات", $"تم سحب سلفة بقيمة {deficit} من الخزنة العامة لإتمام شراء {itemName}");
            }
        }

        [Authorize(Roles = "Admin,PurchasingManager,PurchasingRep,Store")]
        public async Task<IActionResult> Index()
        {
            var purchases = await _context.PurchaseOrders
                .OrderByDescending(p => p.PurchaseDate)
                .ToListAsync();
            return View(purchases);
        }

        [Authorize(Roles = "Admin,PurchasingRep")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,PurchasingRep")]
        public async Task<IActionResult> Create([Bind("ItemName,Quantity,PurchasePrice,SupplierName,SupplierPhone,SupplierLocation,PaymentMethod")] PurchaseOrder purchaseOrder)
        {
            if (ModelState.IsValid)
            {
                purchaseOrder.PurchaseDate = DateTime.Now;
                purchaseOrder.IsReceivedByStore = false;

                // 📌 استدعاء دالة الخصم الذكية
                decimal totalPurchaseCost = purchaseOrder.Quantity * purchaseOrder.PurchasePrice;
                await DeductPurchaseCostAsync(totalPurchaseCost, purchaseOrder.ItemName, purchaseOrder.PaymentMethod);

                _context.Add(purchaseOrder);
                LogAction("إنشاء أمر شراء", $"تم طلب شراء {purchaseOrder.Quantity} من {purchaseOrder.ItemName} عبر {purchaseOrder.PaymentMethod}");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تسجيل المشتريات وخصم التكلفة بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            return View(purchaseOrder);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> MarkAsReceived(int id)
        {
            var purchase = await _context.PurchaseOrders.FindAsync(id);
            if (purchase != null && !purchase.IsReceivedByStore)
            {
                purchase.IsReceivedByStore = true;

                var existingPart = await _context.SpareParts.FirstOrDefaultAsync(p => p.Name == purchase.ItemName);
                if (existingPart != null)
                {
                    existingPart.MainStockQuantity += purchase.Quantity;
                    existingPart.PurchasePrice = purchase.PurchasePrice;

                    if (existingPart.SellingPrice < purchase.PurchasePrice)
                    {
                        existingPart.SellingPrice = purchase.PurchasePrice;
                    }

                    if (!string.IsNullOrEmpty(purchase.SupplierName)) existingPart.SupplierName = purchase.SupplierName;
                    if (!string.IsNullOrEmpty(purchase.SupplierPhone)) existingPart.SupplierPhone = purchase.SupplierPhone;
                    if (!string.IsNullOrEmpty(purchase.SupplierLocation)) existingPart.SupplierLocation = purchase.SupplierLocation;

                    purchase.Barcode = existingPart.PartCode;
                    _context.Update(existingPart);
                }
                else
                {
                    string generatedBarcode = "KBR" + new Random().Next(1000000, 9999999).ToString();

                    var newPart = new SparePart
                    {
                        PartCode = generatedBarcode,
                        Barcode = generatedBarcode,
                        Name = purchase.ItemName,
                        PurchasePrice = purchase.PurchasePrice,
                        SellingPrice = purchase.PurchasePrice,
                        SupplierName = purchase.SupplierName,
                        SupplierPhone = purchase.SupplierPhone,
                        SupplierLocation = purchase.SupplierLocation,
                        MainStockQuantity = purchase.Quantity
                    };
                    _context.SpareParts.Add(newPart);
                    purchase.Barcode = generatedBarcode;
                }

                _context.Update(purchase);
                LogAction("استلام بضاعة بالمخزن", $"تم استلام بضاعة: {purchase.ItemName}");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم استلام البضاعة وإضافتها لأصول المخزون العام بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin,PurchasingManager")]
        public async Task<IActionResult> Pricing()
        {
            var unpricedItems = await _context.PurchaseOrders
                .Where(p => p.IsReceivedByStore && !p.IsPricedByManager)
                .ToListAsync();
            return View(unpricedItems);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,PurchasingManager")]
        public async Task<IActionResult> UpdatePrice(int purchaseId, decimal sellingPrice)
        {
            var purchase = await _context.PurchaseOrders.FindAsync(purchaseId);
            if (purchase != null)
            {
                purchase.IsPricedByManager = true;

                var part = await _context.SpareParts.FirstOrDefaultAsync(p => p.Name == purchase.ItemName);
                if (part != null)
                {
                    part.SellingPrice = sellingPrice;
                    _context.Update(part);
                }

                _context.Update(purchase);
                LogAction("تسعير منتج", $"تم تسعير المنتج {purchase.ItemName} بـ {sellingPrice} ريال");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تسعير المنتج بنجاح!";
            }
            return RedirectToAction(nameof(Pricing));
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var purchase = await _context.PurchaseOrders.FindAsync(id);
            if (purchase != null)
            {
                _context.PurchaseOrders.Remove(purchase);
                LogAction("إلغاء فاتورة شراء", $"تم إلغاء فاتورة المشتريات الخاصة بالصنف {purchase.ItemName}");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف فاتورة المشتريات بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin,PurchasingManager,PurchasingRep,Store")]
        public async Task<IActionResult> PartRequests()
        {
            var requests = await _context.OrderPartRequests
                .Include(r => r.Order)
                    .ThenInclude(o => o.Technician)
                .Where(r => r.RequestType == PartRequestType.PurchaseNew)
                .OrderByDescending(r => r.RequestDate)
                .ToListAsync();

            ViewBag.PendingCount = requests.Count(r => r.Status == PartRequestStatus.PendingPurchasing);
            ViewBag.CompletedCount = requests.Count(r => r.Status == PartRequestStatus.ReadyForInstallation || r.Status == PartRequestStatus.Installed);

            return View(requests);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,PurchasingManager,PurchasingRep,Store")]
        public async Task<IActionResult> ConfirmPurchase(int requestId, decimal purchasePrice, decimal sellingPrice, string supplierName, PaymentMethod paymentMethod)
        {
            var request = await _context.OrderPartRequests.Include(r => r.Order).FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null || request.Status != PartRequestStatus.PendingPurchasing)
            {
                TempData["ErrorMessage"] = "الطلب غير موجود أو تمت معالجته مسبقاً.";
                return RedirectToAction(nameof(PartRequests));
            }

            // 📌 استدعاء دالة الخصم الذكية عند شراء النواقص لتسوية الخزنات
            decimal totalPurchaseCost = request.Quantity * purchasePrice;
            await DeductPurchaseCostAsync(totalPurchaseCost, request.NewPartName, paymentMethod);

            string generatedBarcode = "KBR" + new Random().Next(1000000, 9999999).ToString();

            var newPart = new SparePart
            {
                PartCode = generatedBarcode,
                Barcode = generatedBarcode,
                Name = request.NewPartName,
                TargetModel = request.DeviceModel,
                IsCommon = request.IsCommonRequest,
                PurchasePrice = purchasePrice,
                SellingPrice = sellingPrice,
                SupplierName = supplierName,
                MainStockQuantity = request.Quantity
            };

            _context.SpareParts.Add(newPart);
            await _context.SaveChangesAsync();

            var existingPartByName = await _context.SpareParts.FirstOrDefaultAsync(p => p.Name == request.NewPartName && p.PartId != newPart.PartId);
            if (existingPartByName != null)
            {
                existingPartByName.SellingPrice = sellingPrice;
                existingPartByName.PurchasePrice = purchasePrice;
                _context.Update(existingPartByName);
            }

            request.PartId = newPart.PartId;
            request.Status = PartRequestStatus.ReadyForInstallation;
            _context.Update(request);

            if (request.Order != null)
            {
                request.Order.TechnicianNotes += $"\n[نظام المشتريات]: تم توفير القطعة ({request.NewPartName}) وهي جاهزة للتركيب.";
                _context.Update(request.Order);
            }

            LogAction("توفير نواقص", $"تم إتمام شراء قطعة ناقصة ({request.NewPartName}) للطلب #{request.OrderId} وتسوية الخزنات.");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم الشراء والتكويد بنجاح وخصم المبلغ! تم إشعار الإدارة.";
            return RedirectToAction(nameof(PartRequests));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,PurchasingManager,PurchasingRep")]
        public async Task<IActionResult> RejectPurchase(int requestId, string reason)
        {
            var request = await _context.OrderPartRequests.Include(r => r.Order).FirstOrDefaultAsync(r => r.Id == requestId);
            if (request != null)
            {
                request.Status = PartRequestStatus.Rejected;

                if (request.Order != null)
                {
                    request.Order.TechnicianNotes += $"\n[مرفوض من المشتريات]: تعذر توفير القطعة. السبب: {reason}";
                    _context.Update(request.Order);
                }

                _context.Update(request);

                LogAction("رفض توفير نواقص", $"تم رفض شراء القطعة ({request.NewPartName}) للطلب #{request.OrderId}. السبب: {reason}");

                await _context.SaveChangesAsync();
                TempData["ErrorMessage"] = "تم رفض الطلب وإبلاغ الفني/الإدارة.";
            }
            return RedirectToAction(nameof(PartRequests));
        }
    }
}