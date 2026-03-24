using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace KaberSystem.Controllers
{
    public class InventoryController : Controller
    {
        private readonly ApplicationDbContext _context;

        public InventoryController(ApplicationDbContext context)
        {
            _context = context;
        }

        // عرض المخزون العام لكل قطع الغيار
        public async Task<IActionResult> Index()
        {
            var parts = await _context.SpareParts.ToListAsync();
            return View(parts);
        }

        // شاشة إضافة صنف جديد للمخزن
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,PurchasePrice,SellingPrice,MainStockQuantity")] SparePart sparePart)
        {
            if (ModelState.IsValid)
            {
                _context.Add(sparePart);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم إضافة الصنف الجديد للمخزون بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            return View(sparePart);
        }

        // 📌 شاشة تعديل الصنف
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var part = await _context.SpareParts.FindAsync(id);
            if (part == null) return NotFound();
            return View(part);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> Edit(int id, [Bind("PartId,Name,PurchasePrice,SellingPrice,MainStockQuantity")] SparePart sparePart)
        {
            if (id != sparePart.PartId) return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(sparePart);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "تم تحديث بيانات الصنف بنجاح!";
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

        // 📌 سجل التوالف
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> DamagedParts()
        {
            var damaged = await _context.DamagedParts
                .Include(d => d.SparePart)
                .OrderByDescending(d => d.Date)
                .ToListAsync();
            return View(damaged);
        }

        // 📌 شاشة تسجيل تالف جديد
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> RecordDamage()
        {
            ViewData["PartId"] = new SelectList(await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync(), "PartId", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> RecordDamage([Bind("PartId,Quantity,Reason")] DamagedPart damagedPart)
        {
            var part = await _context.SpareParts.FindAsync(damagedPart.PartId);
            if (part != null && part.MainStockQuantity >= damagedPart.Quantity && damagedPart.Quantity > 0)
            {
                // خصم الكمية التالفة من المخزون الرئيسي
                part.MainStockQuantity -= damagedPart.Quantity;
                _context.Update(part);

                damagedPart.Date = DateTime.Now;
                _context.DamagedParts.Add(damagedPart);

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تسجيل التالف وخصمه من المخزون بنجاح!";
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
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف الصنف من المخزون العام.";
            }
            return RedirectToAction(nameof(Index));
        }

    }
}