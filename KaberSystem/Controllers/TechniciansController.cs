using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace KaberSystem.Controllers
{
    public class TechniciansController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TechniciansController(ApplicationDbContext context)
        {
            _context = context;
        }

        // عرض قائمة الفنيين والموقف الحالي لهم (مشغول / متاح)
        public async Task<IActionResult> Index()
        {
            // جلب جميع الفنيين مع عدد الطلبات المسندة إليهم لمعرفة ضغط العمل لكل فني
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .ToListAsync();

            return View(technicians);
        }

        // عرض تفاصيل الفني شاملة الطلبات الحالية، المكتملة، والعهدة (قطع الغيار المسحوبة له)
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var technician = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Inventory)
                    .ThenInclude(i => i.SparePart) // جلب تفاصيل قطع الغيار المربوطة بالعهدة
                .FirstOrDefaultAsync(m => m.TechnicianId == id);

            if (technician == null) return NotFound();

            // جلب قائمة المخزون العام لإمكانية صرف عهدة جديدة للفني
            ViewData["AvailableParts"] = await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync();

            return View(technician);
        }

        // 📌 شاشة إضافة فني جديد
        [Authorize(Roles = "Admin,CallCenter")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Create([Bind("Name,Phone")] Technician technician)
        {
            ModelState.Remove("Inventory");
            ModelState.Remove("AssignedOrders");
            if (ModelState.IsValid)
            {
                technician.IsAvailable = true;
                technician.TotalIncome = 0;
                _context.Add(technician);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم إضافة الفني الجديد بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            return View(technician);
        }

        // دالة جديدة لصرف قطع غيار (عهدة) للفني
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignStock(int technicianId, int partId, int quantity)
        {
            var technician = await _context.Technicians.Include(t => t.Inventory).FirstOrDefaultAsync(t => t.TechnicianId == technicianId);
            var part = await _context.SpareParts.FindAsync(partId);

            if (technician != null && part != null && part.MainStockQuantity >= quantity)
            {
                // خصم الكمية من المخزن الرئيسي
                part.MainStockQuantity -= quantity;
                _context.Update(part);

                // التحقق مما إذا كان الفني يمتلك هذا الصنف مسبقاً لزيادة الكمية، أو إضافته كصنف جديد
                var existingStock = technician.Inventory.FirstOrDefault(i => i.PartId == partId);
                if (existingStock != null)
                {
                    existingStock.Quantity += quantity;
                    _context.Update(existingStock);
                }
                else
                {
                    _context.TechnicianStocks.Add(new TechnicianStock
                    {
                        TechnicianId = technicianId,
                        PartId = partId,
                        Quantity = quantity
                    });
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم صرف العهدة للفني بنجاح!";
            }
            else
            {
                TempData["ErrorMessage"] = "حدث خطأ! ربما الكمية المطلوبة غير متوفرة في المخزن الرئيسي.";
            }

            return RedirectToAction(nameof(Details), new { id = technicianId });
        }

        // 📌 استرجاع قطعة غيار من عهدة الفني إلى المخزن الرئيسي (مرتجع)
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Store,CallCenter")]
        public async Task<IActionResult> ReturnStock(int technicianId, int partId, int returnQuantity)
        {
            var technicianStock = await _context.TechnicianStocks
                .FirstOrDefaultAsync(ts => ts.TechnicianId == technicianId && ts.PartId == partId);

            if (technicianStock != null && technicianStock.Quantity >= returnQuantity && returnQuantity > 0)
            {
                // 1. خصم من الفني
                technicianStock.Quantity -= returnQuantity;
                if (technicianStock.Quantity == 0)
                {
                    _context.TechnicianStocks.Remove(technicianStock);
                }
                else
                {
                    _context.Update(technicianStock);
                }

                // 2. إعادة للمخزن الرئيسي
                var mainPart = await _context.SpareParts.FindAsync(partId);
                if (mainPart != null)
                {
                    mainPart.MainStockQuantity += returnQuantity;
                    _context.Update(mainPart);
                }

                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم إرجاع القطعة من الفني إلى المخزن بنجاح!";
            }
            else
            {
                TempData["ErrorMessage"] = "فشلت العملية! تأكد من الكمية المراد إرجاعها.";
            }

            return RedirectToAction(nameof(Details), new { id = technicianId });
        }

        // 📌 تقرير أرباح الفنيين
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> IncomeReport()
        {
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .ToListAsync();

            return View(technicians);
        }
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var technician = await _context.Technicians.FindAsync(id);
            if (technician == null) return NotFound();
            return View(technician); // يمكنك إنشاء صفحة Edit.cshtml مشابهة لـ Create.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("TechnicianId,Name,Phone,IsAvailable")] Technician technician)
        {
            if (id != technician.TechnicianId) return NotFound();

            if (ModelState.IsValid)
            {
                _context.Update(technician);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تعديل بيانات الفني بنجاح!";
                return RedirectToAction(nameof(Index)); // أو التوجيه لصفحة التقرير
            }
            return View(technician);
        }

        // 📌 حذف الفني نهائياً
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var technician = await _context.Technicians.FindAsync(id);
            if (technician != null)
            {
                _context.Technicians.Remove(technician);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف الفني وجميع بياناته بنجاح!";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}