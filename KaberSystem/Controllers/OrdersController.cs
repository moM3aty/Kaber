using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;

namespace KaberSystem.Controllers
{
    [Authorize] // حماية كاملة للمتحكم بناءً على تسجيل الدخول
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public OrdersController(ApplicationDbContext context)
        {
            _context = context;
        }

        // عرض جميع الطلبات
        public async Task<IActionResult> Index()
        {
            var orders = await _context.Orders
                .Include(o => o.Technician)
                .OrderByDescending(o => o.CreatedAt)
                .ToListAsync();
            return View(orders);
        }

        // شاشة إنشاء طلب جديد (مخصصة للكول سنتر والإدارة)
        [Authorize(Roles = "Admin,CallCenter")]
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [Authorize(Roles = "Admin,CallCenter")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order order)
        {
            if (ModelState.IsValid)
            {
                order.Status = OrderStatus.New;
                order.CreatedAt = DateTime.Now;
                _context.Add(order);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم إنشاء الطلب بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            return View(order);
        }

        // شاشة تعيين فني للطلب المفتوح
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Assign(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders.FindAsync(id);
            if (order == null) return NotFound();

            ViewData["TechnicianId"] = new SelectList(_context.Technicians.Where(t => t.IsAvailable), "TechnicianId", "Name");
            return View(order);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Assign(int id, int TechnicianId)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                order.TechnicianId = TechnicianId;
                order.Status = OrderStatus.Assigned;
                _context.Update(order);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تعيين الفني بنجاح!";
            }
            return RedirectToAction(nameof(Index));
        }

        // شاشة تأكيد الطلب وتحديد السعر المبدئي
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Confirm(int? id)
        {
            var order = await _context.Orders.Include(o => o.Technician).FirstOrDefaultAsync(m => m.OrderId == id);
            if (order == null || order.Status != OrderStatus.Assigned) return NotFound();

            return View(order);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,CallCenter")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Confirm(int id, DateTime scheduledDate, decimal estimatedPrice, decimal advancePayment)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                // تحديث بيانات الطلب
                order.ScheduledDate = scheduledDate;
                order.EstimatedPrice = estimatedPrice;
                order.AdvancePayment = advancePayment;
                order.Status = OrderStatus.Confirmed;

                // إنشاء الفاتورة المبدئية في الحسابات
                var invoice = new Invoice
                {
                    OrderId = order.OrderId,
                    Amount = advancePayment > 0 ? advancePayment : estimatedPrice,
                    Type = InvoiceType.Advance,
                    Status = InvoiceStatus.NotReceived,
                    IssuedAt = DateTime.Now
                };
                _context.Invoices.Add(invoice);

                _context.Update(order);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تأكيد الطلب وإصدار الفاتورة المبدئية بنجاح.";
            }
            return RedirectToAction("Details", new { id = order?.OrderId });
        }

        // عرض تفاصيل الطلب بالكامل
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .Include(o => o.Technician)
                .Include(o => o.Invoices)
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(p => p.SparePart)
                .FirstOrDefaultAsync(m => m.OrderId == id);

            if (order == null) return NotFound();

            ViewData["AvailableParts"] = await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync();
            return View(order);
        }

        // تحديث حالة الطلب يدوياً
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, OrderStatus newStatus)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                order.Status = newStatus;
                _context.Update(order);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تحديث حالة الطلب بنجاح!";
            }
            return RedirectToAction(nameof(Details), new { id = id });
        }

        // إضافة قطعة غيار للطلب وخصمها من عهدة الفني
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Technician,CallCenter")]
        public async Task<IActionResult> AddUsedPart(int orderId, int partId, int quantity)
        {
            var order = await _context.Orders
                .Include(o => o.Technician)
                    .ThenInclude(t => t.Inventory)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            var part = await _context.SpareParts.FindAsync(partId);

            if (order != null && part != null && order.Technician != null)
            {
                // استخدام ? لتفادي أخطاء الـ Null
                var techStock = order.Technician.Inventory?.FirstOrDefault(i => i.PartId == partId);

                if (techStock != null && techStock.Quantity >= quantity)
                {
                    // 1. خصم الكمية من عهدة الفني
                    techStock.Quantity -= quantity;
                    if (techStock.Quantity == 0)
                        _context.TechnicianStocks.Remove(techStock);
                    else
                        _context.Update(techStock);

                    // 2. إضافة القطعة للطلب
                    var usedPart = new OrderSparePart
                    {
                        OrderId = orderId,
                        PartId = partId,
                        QuantityUsed = quantity,
                        SellingPriceAtTime = part.SellingPrice
                    };
                    _context.UsedSpareParts.Add(usedPart);

                    // 3. تحديث السعر النهائي للطلب
                    order.FinalPrice += (part.SellingPrice * quantity);
                    _context.Update(order);

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "تم إضافة القطعة للفاتورة وخصمها من العهدة بنجاح.";
                }
                else
                {
                    TempData["ErrorMessage"] = "عفواً، الكمية المطلوبة غير متوفرة في عهدة الفني!";
                }
            }

            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        // دالة عرض الفاتورة للعميل لطباعتها
        public async Task<IActionResult> PrintInvoice(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .Include(o => o.Technician)
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(p => p.SparePart)
                .FirstOrDefaultAsync(m => m.OrderId == id);

            if (order == null) return NotFound();

            return View(order);
        }
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var order = await _context.Orders.FindAsync(id);
            if (order == null) return NotFound();
            ViewData["TechnicianId"] = new SelectList(await _context.Technicians.ToListAsync(), "TechnicianId", "Name", order.TechnicianId);
            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, Order order)
        {
            if (id != order.OrderId) return NotFound();
            if (ModelState.IsValid)
            {
                _context.Update(order);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تعديل بيانات الطلب بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            return View(order);
        }

        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                _context.Orders.Remove(order);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف الطلب نهائياً من النظام.";
            }
            return RedirectToAction(nameof(Index));
        }
    }

}