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
    [Authorize(Roles = "Admin,PurchasingManager,PurchasingRep,Store,Purchasing")]
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

        // 📌 التحديث الجذري: خصم المشتريات مباشرة من الخزنة العامة (الكاش أو البنك) لكي يسمّع فوراً عند المحاسب
        private async Task DeductPurchaseCostAsync(decimal totalCost, string itemName, PaymentMethod paymentMethod)
        {
            _context.SafeTransactions.Add(new SafeTransaction
            {
                Amount = totalCost,
                Type = SafeTransactionType.DepositToBank, // تعني سحب / خروج أموال
                TargetSafe = SafeType.General, // 👈 السحب من الخزنة الرئيسية مباشرة
                PaymentMethod = paymentMethod,
                Description = $"دفع قيمة مشتريات (مورد): {itemName}",
                RecordedBy = User.Identity?.Name ?? "System",
                Date = DateTime.Now
            });
            await _context.SaveChangesAsync();
        }

        // 📌 التحديث الجذري: رد الأموال للخزنة العامة عند الحذف أو التعديل
        private void RefundPurchaseCost(decimal totalCost, string itemName, PaymentMethod paymentMethod)
        {
            _context.SafeTransactions.Add(new SafeTransaction
            {
                Amount = totalCost,
                Type = SafeTransactionType.Income, // تعني إيداع / استرجاع أموال
                TargetSafe = SafeType.General, // 👈 الإرجاع للخزنة الرئيسية مباشرة
                PaymentMethod = paymentMethod,
                Description = $"استرداد قيمة شراء (تعديل/إلغاء فاتورة): {itemName}",
                RecordedBy = User.Identity?.Name ?? "System",
                Date = DateTime.Now
            });
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
        public async Task<IActionResult> Create([Bind("ItemName,Quantity,PurchasePrice,TaxAmount,SupplierName,SupplierPhone,SupplierLocation,PaymentMethod")] PurchaseOrder purchaseOrder, IFormFile? invoiceReceipt)
        {
            if (ModelState.IsValid)
            {
                purchaseOrder.PurchaseDate = DateTime.Now;
                purchaseOrder.IsReceivedByStore = false;

                if (invoiceReceipt != null && invoiceReceipt.Length > 0)
                {
                    string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "purchases");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + invoiceReceipt.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await invoiceReceipt.CopyToAsync(fileStream);
                    }
                    purchaseOrder.InvoiceReceiptPath = "/uploads/purchases/" + uniqueFileName;
                }

                decimal totalPurchaseCost = (purchaseOrder.Quantity * purchaseOrder.PurchasePrice) + purchaseOrder.TaxAmount;

                // سحب المبلغ من الخزنة
                await DeductPurchaseCostAsync(totalPurchaseCost, purchaseOrder.ItemName, purchaseOrder.PaymentMethod);

                _context.Add(purchaseOrder);
                LogAction("إنشاء أمر شراء", $"تم طلب شراء {purchaseOrder.Quantity} من {purchaseOrder.ItemName} بتكلفة إجمالية (مع الضريبة) {totalPurchaseCost} عبر {purchaseOrder.PaymentMethod}");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تسجيل المشتريات وخصم التكلفة بنجاح.";
                return RedirectToAction(nameof(Index));
            }
            return View(purchaseOrder);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var purchase = await _context.PurchaseOrders.FindAsync(id);
            if (purchase == null) return NotFound();

            return View(purchase);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, PurchaseOrder updatedPurchase, IFormFile? invoiceReceipt)
        {
            if (id != updatedPurchase.PurchaseId) return NotFound();

            var oldPurchase = await _context.PurchaseOrders.FindAsync(id);
            if (oldPurchase == null) return NotFound();

            decimal oldTotalCost = (oldPurchase.Quantity * oldPurchase.PurchasePrice) + oldPurchase.TaxAmount;
            decimal newTotalCost = (updatedPurchase.Quantity * updatedPurchase.PurchasePrice) + updatedPurchase.TaxAmount;

            // إذا تم تغيير السعر أو طريقة الدفع، نقوم باسترجاع المبلغ القديم وسحب المبلغ الجديد
            if (oldTotalCost != newTotalCost || oldPurchase.PaymentMethod != updatedPurchase.PaymentMethod)
            {
                RefundPurchaseCost(oldTotalCost, oldPurchase.ItemName, oldPurchase.PaymentMethod);
                await DeductPurchaseCostAsync(newTotalCost, updatedPurchase.ItemName, updatedPurchase.PaymentMethod);
            }

            if (oldPurchase.IsReceivedByStore)
            {
                var sparePart = await _context.SpareParts.FirstOrDefaultAsync(p => p.Barcode == oldPurchase.Barcode || p.Name == oldPurchase.ItemName);
                if (sparePart != null)
                {
                    sparePart.MainStockQuantity -= oldPurchase.Quantity;
                    sparePart.MainStockQuantity += updatedPurchase.Quantity;
                    sparePart.PurchasePrice = updatedPurchase.PurchasePrice;
                    _context.Update(sparePart);
                }
            }

            oldPurchase.ItemName = updatedPurchase.ItemName;
            oldPurchase.Quantity = updatedPurchase.Quantity;
            oldPurchase.PurchasePrice = updatedPurchase.PurchasePrice;
            oldPurchase.TaxAmount = updatedPurchase.TaxAmount;
            oldPurchase.SupplierName = updatedPurchase.SupplierName;
            oldPurchase.SupplierPhone = updatedPurchase.SupplierPhone;
            oldPurchase.SupplierLocation = updatedPurchase.SupplierLocation;
            oldPurchase.PaymentMethod = updatedPurchase.PaymentMethod;

            if (invoiceReceipt != null && invoiceReceipt.Length > 0)
            {
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "purchases");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + invoiceReceipt.FileName;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create)) { await invoiceReceipt.CopyToAsync(fileStream); }
                oldPurchase.InvoiceReceiptPath = "/uploads/purchases/" + uniqueFileName;
            }

            _context.Update(oldPurchase);
            LogAction("تعديل فاتورة شراء", $"تم تعديل فاتورة المشتريات للصنف {oldPurchase.ItemName} وتحديث الميزانية والمخزون بناءً على ذلك.");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم تحديث فاتورة الشراء وتعديل الحسابات (والمخزون إن لزم الأمر) بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> MarkAsReceived(int id)
        {
            var purchase = await _context.PurchaseOrders.FindAsync(id);
            if (purchase != null && !purchase.IsReceivedByStore)
            {
                purchase.IsReceivedByStore = true;

                var mainWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.IsMain);

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

                    if (existingPart.WarehouseId == null && mainWarehouse != null)
                    {
                        existingPart.WarehouseId = mainWarehouse.Id;
                    }

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
                        MainStockQuantity = purchase.Quantity,
                        WarehouseId = mainWarehouse?.Id
                    };
                    _context.SpareParts.Add(newPart);
                    purchase.Barcode = generatedBarcode;
                }

                _context.Update(purchase);
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

                var part = await _context.SpareParts.FirstOrDefaultAsync(p => p.Barcode == purchase.Barcode || p.Name == purchase.ItemName);
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
                // رد الأموال للخزنة بالكامل قبل الحذف
                decimal totalCost = (purchase.Quantity * purchase.PurchasePrice) + purchase.TaxAmount;
                RefundPurchaseCost(totalCost, purchase.ItemName, purchase.PaymentMethod);

                if (purchase.IsReceivedByStore)
                {
                    var sparePart = await _context.SpareParts.FirstOrDefaultAsync(p => p.Barcode == purchase.Barcode || p.Name == purchase.ItemName);
                    if (sparePart != null)
                    {
                        sparePart.MainStockQuantity -= purchase.Quantity;
                        if (sparePart.MainStockQuantity < 0) sparePart.MainStockQuantity = 0;
                        _context.Update(sparePart);
                    }
                }

                _context.PurchaseOrders.Remove(purchase);
                LogAction("إلغاء فاتورة شراء", $"تم إلغاء فاتورة المشتريات للصنف {purchase.ItemName} وإرجاع المبلغ ({totalCost}) وخصم الكمية من المخزن.");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف فاتورة المشتريات وإرجاع المبالغ للخزنة (وخصم القطع من المخزون) بنجاح.";
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

            decimal totalPurchaseCost = request.Quantity * purchasePrice;
            await DeductPurchaseCostAsync(totalPurchaseCost, request.NewPartName, paymentMethod);

            string generatedBarcode = "KBR" + new Random().Next(1000000, 9999999).ToString();
            var mainWarehouse = await _context.Warehouses.FirstOrDefaultAsync(w => w.IsMain);

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
                MainStockQuantity = request.Quantity,
                WarehouseId = mainWarehouse?.Id
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