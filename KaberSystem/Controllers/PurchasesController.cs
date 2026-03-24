using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace KaberSystem.Controllers
{
    public class PurchasesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PurchasesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // عرض سجل المشتريات (لمندوب المشتريات والمخزن)
        [Authorize(Roles = "Admin,PurchasingManager,PurchasingRep,Store")]
        public async Task<IActionResult> Index()
        {
            var purchases = await _context.PurchaseOrders
                .OrderByDescending(p => p.PurchaseDate)
                .ToListAsync();
            return View(purchases);
        }

        // شاشة إضافة فاتورة مشتريات جديدة
        [Authorize(Roles = "Admin,PurchasingRep")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,PurchasingRep")]
        public async Task<IActionResult> Create([Bind("ItemName,Quantity,PurchasePrice")] PurchaseOrder purchaseOrder)
        {
            if (ModelState.IsValid)
            {
                purchaseOrder.PurchaseDate = DateTime.Now;
                purchaseOrder.IsReceivedByStore = false; // لم يستلمها المخزن بعد

                _context.Add(purchaseOrder);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تسجيل المشتريات بنجاح. بانتظار اعتماد أمين المخزن.";
                return RedirectToAction(nameof(Index));
            }
            return View(purchaseOrder);
        }

        // دالة للمخزن لتأكيد استلام البضاعة من مندوب المشتريات
        [HttpPost]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> MarkAsReceived(int id)
        {
            var purchase = await _context.PurchaseOrders.FindAsync(id);
            if (purchase != null && !purchase.IsReceivedByStore)
            {
                purchase.IsReceivedByStore = true;

                // هنا يتم إضافة الكمية إلى المخزون العام تلقائياً (SpareParts)
                var existingPart = await _context.SpareParts.FirstOrDefaultAsync(p => p.Name == purchase.ItemName);
                if (existingPart != null)
                {
                    existingPart.MainStockQuantity += purchase.Quantity;
                    // تحديث سعر الشراء إذا لزم الأمر
                    existingPart.PurchasePrice = purchase.PurchasePrice;
                    _context.Update(existingPart);
                }
                else
                {
                    // صنف جديد يضاف للمخزن
                    var newPart = new SparePart
                    {
                        Name = purchase.ItemName,
                        PurchasePrice = purchase.PurchasePrice,
                        SellingPrice = purchase.PurchasePrice, // السعر المبدئي حتى يقوم مدير المشتريات بتسعيره
                        MainStockQuantity = purchase.Quantity
                    };
                    _context.SpareParts.Add(newPart);
                }

                _context.Update(purchase);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم استلام البضاعة وإضافتها للمخزون العام بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }

        // 📌 شاشة تسعير مدير المشتريات (تحديد سعر البيع وهامش الربح)
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