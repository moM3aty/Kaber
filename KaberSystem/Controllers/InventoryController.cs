// مسار الملف: Controllers/InventoryController.cs
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
    [Authorize(Roles = "Admin,Store,Inventory")]
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

        // 📌 التحديث: عرض المنتجات (مع فرز المنتجات المخلصة للأسفل وإخفاء المحذوفة)
        public async Task<IActionResult> Index(string searchQuery, int? warehouseId)
        {
            ViewData["CurrentSearch"] = searchQuery;
            ViewData["CurrentWarehouse"] = warehouseId;
            ViewData["WarehousesList"] = new SelectList(await _context.Warehouses.ToListAsync(), "Id", "Name");

            var query = _context.SpareParts
                .Include(p => p.Warehouse)
                .Where(p => !p.IsDeleted) // إخفاء المحذوف
                .AsQueryable();

            if (warehouseId.HasValue)
            {
                query = query.Where(p => p.WarehouseId == warehouseId);
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(p =>
                    p.Name.Contains(searchQuery) ||
                    (p.PartCode != null && p.PartCode.Contains(searchQuery)) ||
                    (p.TargetModel != null && p.TargetModel.Contains(searchQuery))
                );
            }

            // 📌 التحديث: الترتيب الذكي (الكمية الأكبر من 0 تظهر فوق، المخلّص ينزل تحت)
            var parts = await query
                .OrderByDescending(p => p.MainStockQuantity > 0)
                .ThenByDescending(p => p.PartId)
                .ToListAsync();

            return View(parts);
        }

        // 📌 التحديث: إدارة المستودعات الرئيسية والفرعية
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Warehouses()
        {
            var w = await _context.Warehouses.ToListAsync();
            return View(w);
        }

        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> CreateWarehouse(string name, string location, bool isMain)
        {
            _context.Warehouses.Add(new Warehouse { Name = name, Location = location, IsMain = isMain });
            await _context.SaveChangesAsync();
            LogAction("إضافة مستودع", $"تم إنشاء مستودع جديد: {name}");
            TempData["SuccessMessage"] = "تم إنشاء المستودع بنجاح.";
            return RedirectToAction(nameof(Warehouses));
        }

        public async Task<IActionResult> Create()
        {
            ViewData["WarehouseId"] = new SelectList(await _context.Warehouses.ToListAsync(), "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("PartCode,Name,IsCommon,TargetModel,PurchasePrice,SellingPrice,MainStockQuantity,SupplierName,SupplierPhone,SupplierLocation,WarehouseId")] SparePart sparePart)
        {
            if (string.IsNullOrEmpty(sparePart.PartCode))
            {
                sparePart.PartCode = "KBR-" + new Random().Next(100000, 999999).ToString();
            }

            if (sparePart.IsCommon)
            {
                sparePart.TargetModel = null;
            }

            // إذا لم يحدد مستودع، ابحث عن المستودع الرئيسي
            if (!sparePart.WarehouseId.HasValue)
            {
                var mainW = await _context.Warehouses.FirstOrDefaultAsync(w => w.IsMain);
                if (mainW != null) sparePart.WarehouseId = mainW.Id;
            }

            if (ModelState.IsValid)
            {
                _context.Add(sparePart);
                LogAction("إضافة صنف", $"تم تعريف صنف جديد بالمخزن: {sparePart.Name} بكمية {sparePart.MainStockQuantity}");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم إضافة الصنف وتكويده في المخزون بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            ViewData["WarehouseId"] = new SelectList(await _context.Warehouses.ToListAsync(), "Id", "Name", sparePart.WarehouseId);
            return View(sparePart);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var part = await _context.SpareParts.FindAsync(id);
            if (part == null) return NotFound();

            ViewData["WarehouseId"] = new SelectList(await _context.Warehouses.ToListAsync(), "Id", "Name", part.WarehouseId);
            return View(part);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("PartId,PartCode,Name,IsCommon,TargetModel,PurchasePrice,SellingPrice,MainStockQuantity,SupplierName,SupplierPhone,SupplierLocation,WarehouseId")] SparePart sparePart)
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
                    existingPart.WarehouseId = sparePart.WarehouseId;

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
            ViewData["WarehouseId"] = new SelectList(await _context.Warehouses.ToListAsync(), "Id", "Name", sparePart.WarehouseId);
            return View(sparePart);
        }

        // 📌 التحديث: شاشة جرد المستودع الفعلي
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> Stocktake()
        {
            var parts = await _context.SpareParts
                .Where(p => !p.IsDeleted)
                .OrderByDescending(p => p.MainStockQuantity > 0)
                .ToListAsync();
            return View(parts);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> SubmitStocktake(int partId, int actualQuantity, string notes)
        {
            var part = await _context.SpareParts.FindAsync(partId);
            if (part != null)
            {
                int difference = actualQuantity - part.MainStockQuantity;

                if (difference < 0)
                {
                    // عجز (تالف أو مفقود)
                    int lossQuantity = Math.Abs(difference);
                    _context.DamagedParts.Add(new DamagedPart
                    {
                        PartId = part.PartId,
                        Quantity = lossQuantity,
                        Reason = $"[تسوية جردية]: عجز في الرصيد. الملاحظات: {notes}",
                        Date = DateTime.Now,
                        TotalLoss = lossQuantity * part.PurchasePrice
                    });
                }

                part.MainStockQuantity = actualQuantity;
                _context.Update(part);

                LogAction("جرد مخزون", $"تم تسوية جرد الصنف {part.Name}. الرصيد الفعلي المسجل: {actualQuantity}");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم اعتماد الجرد وتسوية الرصيد بنجاح.";
            }
            return RedirectToAction(nameof(Stocktake));
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
            ViewData["PartId"] = new SelectList(await _context.SpareParts.Where(p => p.MainStockQuantity > 0 && !p.IsDeleted).ToListAsync(), "PartId", "Name");
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
            ViewData["PartId"] = new SelectList(await _context.SpareParts.Where(p => p.MainStockQuantity > 0 && !p.IsDeleted).ToListAsync(), "PartId", "Name", damagedPart.PartId);
            return View(damagedPart);
        }

        // 📌 التحديث: حذف آمن (Soft Delete) لعدم تدمير الحسابات
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var part = await _context.SpareParts.FindAsync(id);
            if (part != null)
            {
                part.IsDeleted = true; // إخفاء فقط
                part.MainStockQuantity = 0; // تصفير الرصيد
                _context.Update(part);

                LogAction("حذف صنف آمن", $"تم أرشفة وحذف الصنف ({part.Name}) مع تصفير رصيده، لضمان عدم تأثر الفواتير القديمة.");

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم أرشفة وحذف الصنف بنجاح وبشكل آمن يحفظ الدفاتر المحاسبية.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}