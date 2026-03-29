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
    [Authorize]
    public class OrdersController : Controller
    {
        private readonly ApplicationDbContext _context;

        public OrdersController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string searchQuery)
        {
            ViewData["CurrentSearch"] = searchQuery; 

            var query = _context.Orders.Include(o => o.Technician).AsQueryable();

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(o => o.CustomerName.Contains(searchQuery) || o.PhoneNumber.Contains(searchQuery));
            }

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();
            return View(orders);
        }

        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Create()
        {
            ViewData["TechnicianId"] = new SelectList(await _context.Technicians.Where(t => t.IsAvailable).ToListAsync(), "TechnicianId", "Name");
            return View();
        }

        [HttpPost]
        [Authorize(Roles = "Admin,CallCenter")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order order)
        {
            if (ModelState.IsValid)
            {
                order.Status = order.TechnicianId.HasValue ? OrderStatus.Assigned : OrderStatus.New;
                order.CreatedAt = DateTime.Now;
                _context.Add(order);
                await _context.SaveChangesAsync();

                if (order.EstimatedPrice > 0)
                {
                    var invoice = new Invoice
                    {
                        OrderId = order.OrderId,
                        Amount = order.EstimatedPrice,
                        Type = InvoiceType.Advance,
                        Status = InvoiceStatus.NotReceived,
                        IssuedAt = DateTime.Now
                    };
                    _context.Invoices.Add(invoice);
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = "تم إنشاء الطلب بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            ViewData["TechnicianId"] = new SelectList(await _context.Technicians.Where(t => t.IsAvailable).ToListAsync(), "TechnicianId", "Name", order.TechnicianId);
            return View(order);
        }

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
                order.ScheduledDate = scheduledDate;
                order.EstimatedPrice = estimatedPrice;
                order.AdvancePayment = advancePayment;
                order.Status = OrderStatus.Confirmed;

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


        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .Include(o => o.Technician)
                .Include(o => o.UsedSpareParts).ThenInclude(up => up.SparePart)
                .FirstOrDefaultAsync(m => m.OrderId == id);

            if (order == null) return NotFound();

            ViewBag.PartRequests = await _context.OrderPartRequests
                .Include(pr => pr.SparePart)
                .Where(pr => pr.OrderId == id)
                .OrderByDescending(pr => pr.RequestDate)
                .ToListAsync();

            ViewData["AvailableParts"] = await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync();

            return View(order);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestFromWarehouse(int orderId, int partId, int quantity)
        {
            var request = new OrderPartRequest
            {
                OrderId = orderId,
                RequestType = PartRequestType.FromWarehouse,
                PartId = partId,
                Quantity = quantity,
                Status = PartRequestStatus.PendingWarehouse
            };

            _context.OrderPartRequests.Add(request);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم طلب القطعة من المخزن بنجاح.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RequestPurchase(int orderId, string partName, string deviceModel, int quantity, bool isCommon, IFormFile partImage)
        {
            string imagePath = null;
            if (partImage != null && partImage.Length > 0)
            {
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "parts");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + partImage.FileName;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create)) { await partImage.CopyToAsync(fileStream); }
                imagePath = "/uploads/parts/" + uniqueFileName;
            }

            var request = new OrderPartRequest
            {
                OrderId = orderId,
                RequestType = PartRequestType.PurchaseNew,
                NewPartName = partName,
                DeviceModel = deviceModel,
                IsCommonRequest = isCommon,
                Quantity = quantity,
                ImagePath = imagePath,
                Status = PartRequestStatus.PendingPurchasing
            };

            _context.OrderPartRequests.Add(request);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "تم إرسال طلب الشراء. سيتم تكويد القطعة وربطها بالموديل عند الشراء.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }
        [HttpPost]
        public async Task<IActionResult> UpdateStatus(int id, OrderStatus newStatus, string technicianNotes, int isFeeApplied)
        {
            var order = await _context.Orders
                .Include(o => o.UsedSpareParts)
                .Include(o => o.Invoices)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order != null)
            {
                order.Status = newStatus;
                order.TechnicianNotes = technicianNotes;
                order.IsFeeApplied = (isFeeApplied == 1);

                // إعادة حساب السعر النهائي بناءً على خيار (رسوم الفحص)
                decimal partsTotal = order.UsedSpareParts?.Sum(p => p.QuantityUsed * p.SellingPriceAtTime) ?? 0;
                decimal appliedFee = order.IsFeeApplied ? order.EstimatedPrice : 0;

                order.FinalPrice = appliedFee + partsTotal - order.AdvancePayment;

                // التسميع في الحسابات: إذا أصبح الطلب (مكتمل)
                if (newStatus == OrderStatus.Completed)
                {
                    var finalInvoice = order.Invoices?.FirstOrDefault(i => i.Type == InvoiceType.Final);

                    // إذا لم تكن هناك فاتورة نهائية، قم بإنشائها لترحل للحسابات
                    if (finalInvoice == null)
                    {
                        var invoice = new Invoice
                        {
                            OrderId = order.OrderId,
                            Amount = order.FinalPrice > 0 ? order.FinalPrice : 0,
                            Type = InvoiceType.Final,
                            Status = InvoiceStatus.NotReceived, // تذهب للحسابات بانتظار التحصيل
                            IssuedAt = DateTime.Now
                        };
                        _context.Invoices.Add(invoice);
                    }
                    else
                    {
                        // تحديث المبلغ إذا تم تعديل الطلب المكتمل مسبقاً
                        finalInvoice.Amount = order.FinalPrice > 0 ? order.FinalPrice : 0;
                        _context.Update(finalInvoice);
                    }
                }

                _context.Update(order);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم تحديث الطلب، وتطبيق الحسابات بنجاح!";
            }
            return RedirectToAction(nameof(Details), new { id = id });
        }

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
                var techStock = order.Technician.Inventory?.FirstOrDefault(i => i.PartId == partId);

                if (techStock != null && techStock.Quantity >= quantity)
                {
                    techStock.Quantity -= quantity;
                    if (techStock.Quantity == 0)
                        _context.TechnicianStocks.Remove(techStock);
                    else
                        _context.Update(techStock);

                    var usedPart = new OrderSparePart
                    {
                        OrderId = orderId,
                        PartId = partId,
                        QuantityUsed = quantity,
                        SellingPriceAtTime = part.SellingPrice
                    };
                    _context.UsedSpareParts.Add(usedPart);

                    // تحديث السعر النهائي للطلب
                    decimal appliedFee = order.IsFeeApplied ? order.EstimatedPrice : 0;
                    order.FinalPrice = appliedFee + (order.UsedSpareParts.Sum(p => p.QuantityUsed * p.SellingPriceAtTime)) + (part.SellingPrice * quantity) - order.AdvancePayment;
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
        [Authorize(Roles = "Admin,CallCenter,Technician")]
        public async Task<IActionResult> Quotation(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders
                .Include(o => o.Technician)
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(up => up.SparePart)
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
            ViewData["TechnicianId"] = new SelectList(await _context.Technicians.ToListAsync(), "TechnicianId", "Name", order.TechnicianId);
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
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Technician,CallCenter")]
        public async Task<IActionResult> RemoveUsedPart(int orderId, int recordId)
        {
            var usedPart = await _context.UsedSpareParts
                .Include(up => up.Order)
                .FirstOrDefaultAsync(up => up.Id == recordId && up.OrderId == orderId);

            if (usedPart == null) return NotFound();

            var order = usedPart.Order;

            // 📌 الحماية الأمنية: يمنع الحذف إذا كان الطلب مكتمل إلا للإدارة
            if (order.Status == OrderStatus.Completed && !User.IsInRole("Admin"))
            {
                TempData["ErrorMessage"] = "مغلق! لا يمكن حذف القطع بعد اكتمال الطلب إلا من قبل الإدارة.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            // 1. إرجاع الكمية لعهدة الفني
            if (order.TechnicianId.HasValue)
            {
                var techStock = await _context.TechnicianStocks
                    .FirstOrDefaultAsync(ts => ts.TechnicianId == order.TechnicianId && ts.PartId == usedPart.PartId);

                if (techStock != null)
                {
                    techStock.Quantity += usedPart.QuantityUsed;
                    _context.Update(techStock);
                }
                else
                {
                    _context.TechnicianStocks.Add(new TechnicianStock
                    {
                        TechnicianId = order.TechnicianId.Value,
                        PartId = usedPart.PartId,
                        Quantity = usedPart.QuantityUsed
                    });
                }
            }

            // 2. خصم قيمة القطعة من الفاتورة النهائية
            order.FinalPrice -= (usedPart.QuantityUsed * usedPart.SellingPriceAtTime);
            _context.Update(order);

            // 3. حذف سجل الاستخدام
            _context.UsedSpareParts.Remove(usedPart);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف القطعة، إرجاعها للعهدة، وتحديث الفاتورة بنجاح.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }
    }
}