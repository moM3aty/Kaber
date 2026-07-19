// مسار الملف: Controllers/TechniciansController.cs
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,CallCenter,Technicians,Orders,Technician")]
    public class TechniciansController : Controller
    {
        private readonly ApplicationDbContext _context;

        public TechniciansController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .ToListAsync();

            return View(technicians);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var technician = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Inventory)
                    .ThenInclude(i => i.SparePart)
                .FirstOrDefaultAsync(m => m.TechnicianId == id);

            if (technician == null) return NotFound();

            ViewData["AvailableParts"] = await _context.SpareParts.Where(p => p.MainStockQuantity > 0 && !p.IsDeleted).ToListAsync();

            return View(technician);
        }

        // 📌 التحديث: البحث عن الفني بشكل غير حساس لحالة الأحرف
        [Authorize(Roles = "Technician")]
        public async Task<IActionResult> MyStock()
        {
            string loggedInUser = User.Identity.Name?.Trim() ?? "";

            var myAccount = await _context.Technicians
                .Include(t => t.Inventory)
                    .ThenInclude(i => i.SparePart)
                .FirstOrDefaultAsync(t => t.Name == loggedInUser);

            if (myAccount == null)
            {
                return NotFound("عذراً، لم يتم العثور على ملف فني مرتبط باسم الدخول الخاص بك. الرجاء من الإدارة إنشاء ملف لك في شاشة الفنيين بنفس اسم الدخول.");
            }

            return View(myAccount);
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
            ModelState.Remove("Expenses");

            if (ModelState.IsValid)
            {
                technician.Name = technician.Name.Trim(); // منع المسافات الزائدة
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

        [Authorize(Roles = "Admin,Accounting")]
        public async Task<IActionResult> IncomeReport()
        {
            var technicians = await _context.Technicians
                .Include(t => t.AssignedOrders)
                .Include(t => t.Expenses)
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
                technician.Name = technician.Name.Trim();
                _context.Update(technician);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تعديل بيانات الفني بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            return View(technician);
        }

        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var technician = await _context.Technicians
                .Include(t => t.Inventory)
                .Include(t => t.AssignedOrders)
                .Include(t => t.Expenses)
                .FirstOrDefaultAsync(t => t.TechnicianId == id);

            if (technician != null)
            {
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

                if (technician.AssignedOrders != null && technician.AssignedOrders.Any())
                {
                    foreach (var order in technician.AssignedOrders)
                    {
                        order.TechnicianId = null;
                        string noteHeader = string.IsNullOrEmpty(order.TechnicianNotes) ? "" : "\n----------------\n";
                        order.TechnicianNotes += $"{noteHeader}[نظام الإدارة]: تم حذف حساب الفني ({technician.Name}) الذي كان مكلفاً بهذا الطلب.";
                        _context.Update(order);
                    }
                }

                if (technician.Expenses != null && technician.Expenses.Any())
                {
                    foreach (var exp in technician.Expenses)
                    {
                        exp.TechnicianId = null;
                        exp.Description += $" [كانت مسجلة على الفني المحذوف: {technician.Name}]";
                        _context.Update(exp);
                    }
                }

                _context.Technicians.Remove(technician);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف الفني بنجاح، وإرجاع عهدته للمخزن، والاحتفاظ بطلباته وحساباته بأمان!";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}