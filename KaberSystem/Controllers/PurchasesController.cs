using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;

namespace KaberSystem.Controllers
{
    public class PurchasesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PurchasesController(ApplicationDbContext context)
        {
            _context = context;
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
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تسجيل المشتريات بنجاح. بانتظار اعتماد أمين المخزن.";
                return RedirectToAction(nameof(Index));
            }
            return View(purchaseOrder);
        }

        // 📌 دالة الاستلام: تم إضافة توليد الباركود الآلي هنا
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

                    // 📌 ربط الباركود للمنتج الموجود
                    purchase.Barcode = existingPart.Barcode;

                    _context.Update(existingPart);
                }
                else
                {
                    // 📌 توليد باركود فريد للصنف الجديد (KBR + 7 أرقام عشوائية)
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
                        Barcode = generatedBarcode // حفظ الباركود في المخزن
                    };
                    _context.SpareParts.Add(newPart);

                    purchase.Barcode = generatedBarcode; // حفظ الباركود في الفاتورة للطباعة
                }

                _context.Update(purchase);
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
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف فاتورة المشتريات بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}