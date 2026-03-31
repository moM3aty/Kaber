using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;

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
        public async Task<IActionResult> Create([Bind("ItemName,Quantity,PurchasePrice,SupplierName,SupplierPhone,SupplierLocation")] PurchaseOrder purchaseOrder)
        {
            if (ModelState.IsValid)
            {
                purchaseOrder.PurchaseDate = DateTime.Now;
                purchaseOrder.IsReceivedByStore = false;

                _context.Add(purchaseOrder);

                // 📌 تسجيل الحركة
                LogAction("إنشاء أمر شراء", $"تم طلب شراء {purchaseOrder.Quantity} من {purchaseOrder.ItemName}");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تسجيل المشتريات بنجاح. بانتظار اعتماد أمين المخزن.";
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
                        Name = purchase.ItemName,
                        PurchasePrice = purchase.PurchasePrice,
                        SellingPrice = purchase.PurchasePrice,
                        MainStockQuantity = purchase.Quantity,
                        SupplierName = purchase.SupplierName,
                        SupplierPhone = purchase.SupplierPhone,
                        SupplierLocation = purchase.SupplierLocation,
                        PartCode = generatedBarcode,
                        Barcode = generatedBarcode
                    };
                    _context.SpareParts.Add(newPart);
                    purchase.Barcode = generatedBarcode;
                }

                _context.Update(purchase);

                // 📌 تسجيل الحركة
                LogAction("استلام بضاعة بالمخزن", $"تم استلام بضاعة: {purchase.ItemName} وإضافتها للمخزن الفعلي");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم استلام البضاعة وإضافتها للمخزون العام بنجاح.";
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

                // 📌 تسجيل الحركة
                LogAction("تسعير منتج", $"تم تسعير المنتج {purchase.ItemName} بـ {sellingPrice} ريال للعميل");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تسعير المنتج واعتماد هامش الربح بنجاح!";
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

                // 📌 تسجيل الحركة
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
        public async Task<IActionResult> ConfirmPurchase(int requestId, decimal purchasePrice, decimal sellingPrice, string supplierName)
        {
            var request = await _context.OrderPartRequests.Include(r => r.Order).FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null || request.Status != PartRequestStatus.PendingPurchasing)
            {
                TempData["ErrorMessage"] = "الطلب غير موجود أو تمت معالجته مسبقاً.";
                return RedirectToAction(nameof(PartRequests));
            }

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

            request.PartId = newPart.PartId;
            request.Status = PartRequestStatus.ReadyForInstallation;

            _context.Update(request);

            if (request.Order != null)
            {
                request.Order.TechnicianNotes += $"\n[نظام المشتريات]: تم توفير القطعة ({request.NewPartName}) وهي جاهزة للتركيب.";
                _context.Update(request.Order);
            }

            // 📌 تسجيل الحركة
            LogAction("توفير نواقص", $"تم إتمام شراء قطعة ناقصة ({request.NewPartName}) للطلب #{request.OrderId}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم الشراء والتكويد بنجاح! تم إشعار الكول سنتر لتحديد موعد التركيب.";
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

                // 📌 تسجيل الحركة
                LogAction("رفض توفير نواقص", $"تم رفض شراء القطعة ({request.NewPartName}) للطلب #{request.OrderId}. السبب: {reason}");

                await _context.SaveChangesAsync();
                TempData["ErrorMessage"] = "تم رفض الطلب وإبلاغ الفني/الإدارة.";
            }
            return RedirectToAction(nameof(PartRequests));
        }
    }
}