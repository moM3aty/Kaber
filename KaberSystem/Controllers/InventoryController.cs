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
    [Authorize(Roles = "Admin,Store")]
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;

        public InventoryController(ApplicationDbContext context)
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

        // 📌 التحديث هنا: إضافة خاصية البحث الشامل في المخزون
        public async Task<IActionResult> Index(string searchQuery)
        {
            ViewData["CurrentSearch"] = searchQuery;

            var query = _context.SpareParts.AsQueryable();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(p =>
                    p.Name.Contains(searchQuery) ||
                    (p.PartCode != null && p.PartCode.Contains(searchQuery)) ||
                    (p.TargetModel != null && p.TargetModel.Contains(searchQuery)) ||
                    (p.SupplierName != null && p.SupplierName.Contains(searchQuery))
                );
            }

            var parts = await query.OrderByDescending(p => p.PartId).ToListAsync();
            return View(parts);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PartCode,Name,IsCommon,TargetModel,PurchasePrice,SellingPrice,MainStockQuantity,SupplierName,SupplierPhone,SupplierLocation")] SparePart sparePart)
        {
            if (string.IsNullOrEmpty(sparePart.PartCode))
            {
                sparePart.PartCode = "KBR-" + new Random().Next(100000, 999999).ToString();
            }

            if (sparePart.IsCommon)
            {
                sparePart.TargetModel = null;
            }

            if (ModelState.IsValid)
            {
                _context.Add(sparePart);
                LogAction("إضافة صنف", $"تم تعريف صنف جديد بالمخزن: {sparePart.Name} بكمية {sparePart.MainStockQuantity}");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم إضافة الصنف وتكويده في المخزون بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            return View(sparePart);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var part = await _context.SpareParts.FindAsync(id);
            if (part == null) return NotFound();
            return View(part);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("PartId,PartCode,Name,IsCommon,TargetModel,PurchasePrice,SellingPrice,MainStockQuantity,SupplierName,SupplierPhone,SupplierLocation")] SparePart sparePart)
        {
            if (id != sparePart.PartId) return NotFound();

            ModelState.Remove("PartCode");
            ModelState.Remove("Name");

            if (ModelState.IsValid)
            {
                try
                {
                    var existingPart = await _context.SpareParts.FindAsync(id);
                    if (existingPart == null) return NotFound();

                    if (!string.IsNullOrEmpty(sparePart.PartCode)) existingPart.PartCode = sparePart.PartCode;
                    if (!string.IsNullOrEmpty(sparePart.Name)) existingPart.Name = sparePart.Name;

                    existingPart.IsCommon = sparePart.IsCommon;
                    existingPart.TargetModel = sparePart.IsCommon ? null : sparePart.TargetModel;
                    existingPart.PurchasePrice = sparePart.PurchasePrice;
                    existingPart.SellingPrice = sparePart.SellingPrice;

                    existingPart.SupplierName = sparePart.SupplierName;
                    existingPart.SupplierPhone = sparePart.SupplierPhone;
                    existingPart.SupplierLocation = sparePart.SupplierLocation;

                    if (User.IsInRole("Admin"))
                    {
                        existingPart.MainStockQuantity = sparePart.MainStockQuantity;
                    }

                    _context.Update(existingPart);
                    LogAction("تعديل صنف", $"تم تعديل بيانات الصنف: {existingPart.Name} وتحديث الكميات والأسعار");

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "تم تحديث بيانات وتصنيف الصنف بنجاح!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.SpareParts.Any(e => e.PartId == sparePart.PartId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(sparePart);
        }

        public async Task<IActionResult> DamagedParts()
        {
            var damaged = await _context.DamagedParts
                .Include(d => d.SparePart)
                .OrderByDescending(d => d.Date)
                .ToListAsync();
            return View(damaged);
        }

        public async Task<IActionResult> RecordDamage()
        {
            ViewData["PartId"] = new SelectList(await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync(), "PartId", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordDamage([Bind("PartId,Quantity,Reason")] DamagedPart damagedPart)
        {
            var part = await _context.SpareParts.FindAsync(damagedPart.PartId);
            if (part != null && part.MainStockQuantity >= damagedPart.Quantity && damagedPart.Quantity > 0)
            {
                part.MainStockQuantity -= damagedPart.Quantity;
                _context.Update(part);

                damagedPart.Date = DateTime.Now;
                damagedPart.TotalLoss = damagedPart.Quantity * part.PurchasePrice;

                _context.DamagedParts.Add(damagedPart);
                LogAction("تسجيل تالف", $"إتلاف {damagedPart.Quantity} من {part.Name} بخسارة مالية قدرها {damagedPart.TotalLoss} ريال. السبب: {damagedPart.Reason}");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"تم خصم الكمية وتسجيل الخسارة المالية ({damagedPart.TotalLoss} ريال) بنجاح!";
                return RedirectToAction(nameof(DamagedParts));
            }

            TempData["ErrorMessage"] = "حدث خطأ! تأكد من أن الكمية المدخلة متوفرة في المخزن.";
            ViewData["PartId"] = new SelectList(await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync(), "PartId", "Name", damagedPart.PartId);
            return View(damagedPart);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var part = await _context.SpareParts.FindAsync(id);
            if (part != null)
            {
                _context.SpareParts.Remove(part);
                LogAction("حذف صنف", $"تم حذف الصنف ({part.Name}) نهائياً من المخزون العام");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف الصنف من المخزون العام.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}