using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,CallCenter,Technicians,Orders")]
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
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .ToListAsync();

            return View(technicians);
        }

        // عرض تفاصيل الفني شاملة الطلبات الحالية، المكتملة، والعهدة
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var technician = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Inventory)
                    .ThenInclude(i => i.SparePart)
                .FirstOrDefaultAsync(m => m.TechnicianId == id);

            if (technician == null) return NotFound();

            ViewData["AvailableParts"] = await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync();

            return View(technician);
        }

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
            ModelState.Remove("Expenses"); // إصلاح مشكلة الـ Validation

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AssignStock(int technicianId, int partId, int quantity)
        {
            var technician = await _context.Technicians.Include(t => t.Inventory).FirstOrDefaultAsync(t => t.TechnicianId == technicianId);
            var part = await _context.SpareParts.FindAsync(partId);

            if (technician != null && part != null && part.MainStockQuantity >= quantity)
            {
                part.MainStockQuantity -= quantity;
                _context.Update(part);

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

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Store,CallCenter")]
        public async Task<IActionResult> ReturnStock(int technicianId, int partId, int returnQuantity)
        {
            var technicianStock = await _context.TechnicianStocks
                .FirstOrDefaultAsync(ts => ts.TechnicianId == technicianId && ts.PartId == partId);

            if (technicianStock != null && technicianStock.Quantity >= returnQuantity && returnQuantity > 0)
            {
                technicianStock.Quantity -= returnQuantity;
                if (technicianStock.Quantity == 0)
                {
                    _context.TechnicianStocks.Remove(technicianStock);
                }
                else
                {
                    _context.Update(technicianStock);
                }

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

        // 📌 حل المشكلة 1: جلب المصروفات لحل مشكلة انهيار تقرير الأرباح
        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> IncomeReport()
        {
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Expenses) // 👈 هذا السطر كان مفقوداً ويسبب الخطأ
                .ToListAsync();

            return View(technicians);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var technician = await _context.Technicians.FindAsync(id);
            if (technician == null) return NotFound();
            return View(technician);
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
                return RedirectToAction(nameof(Index));
            }
            return View(technician);
        }

        // 📌 حل مشكلة الحذف الآمن: فك الارتباط بالطلبات، المصروفات، وإرجاع العهدة
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            // جلب الفني مع كافة متعلقاته المرتبطة في قاعدة البيانات لتجنب خطأ Foreign Key Constraint
            var technician = await _context.Technicians
                .Include(t => t.Inventory)
                .Include(t => t.AssignedOrders)
                .Include(t => t.Expenses)
                .FirstOrDefaultAsync(t => t.TechnicianId == id);

            if (technician != null)
            {
                // 1. إرجاع قطع الغيار من عهدة الفني إلى المخزن العام لكي لا تضيع الأصول
                if (technician.Inventory != null && technician.Inventory.Any())
                {
                    foreach (var stock in technician.Inventory)
                    {
                        var mainPart = await _context.SpareParts.FindAsync(stock.PartId);
                        if (mainPart != null)
                        {
                            mainPart.MainStockQuantity += stock.Quantity;
                            _context.Update(mainPart);
                        }
                    }
                    _context.TechnicianStocks.RemoveRange(technician.Inventory);
                }

                // 2. فك ارتباط الفني بالطلبات (حماية الطلبات من الحذف وإضافة ملاحظة للتوضيح)
                if (technician.AssignedOrders != null && technician.AssignedOrders.Any())
                {
                    foreach (var order in technician.AssignedOrders)
                    {
                        order.TechnicianId = null; // إزالة الفني ليصبح الطلب معلقاً

                        string noteHeader = string.IsNullOrEmpty(order.TechnicianNotes) ? "" : "\n----------------\n";
                        order.TechnicianNotes += $"{noteHeader}[نظام الإدارة]: تم حذف حساب الفني ({technician.Name}) الذي كان مكلفاً بهذا الطلب.";

                        _context.Update(order);
                    }
                }

                // 3. فك ارتباط الفني بالمصروفات والسلف (حماية السجلات المحاسبية)
                if (technician.Expenses != null && technician.Expenses.Any())
                {
                    foreach (var exp in technician.Expenses)
                    {
                        exp.TechnicianId = null; // فك الارتباط
                        exp.Description += $" [كانت مسجلة على الفني المحذوف: {technician.Name}]";
                        _context.Update(exp);
                    }
                }

                // 4. حذف سجل الفني بسلام
                _context.Technicians.Remove(technician);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف الفني بنجاح، وإرجاع عهدته للمخزن، والاحتفاظ بطلباته وحساباته بأمان!";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}