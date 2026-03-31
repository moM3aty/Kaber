using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Collections.Generic;

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

        public async Task<IActionResult> Index(string searchQuery)
        {
            ViewData["CurrentSearch"] = searchQuery;

            var query = _context.Orders.Include(o => o.Technician).AsQueryable();

            if (User.IsInRole("Technician"))
            {
                query = query.Where(o => o.Technician.Name == User.Identity.Name
                                      && o.Status != OrderStatus.Completed
                                      && o.Status != OrderStatus.Approved
                                      && o.Status != OrderStatus.Cancelled);
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(o => o.CustomerName.Contains(searchQuery) || o.PhoneNumber.Contains(searchQuery));
            }

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

            if (User.IsInRole("Technician"))
            {
                ViewBag.NewOrdersCount = orders.Count(o => o.Status == OrderStatus.Assigned || o.Status == OrderStatus.Returned);
            }

            // 📌 الميزة الجديدة: البحث عن الطلبات التي توفرت قطع غيارها (للكول سنتر والإدارة)
            if (User.IsInRole("Admin") || User.IsInRole("CallCenter"))
            {
                var readyPartOrders = await _context.OrderPartRequests
                    .Where(pr => pr.Status == PartRequestStatus.ReadyForInstallation)
                    .Select(pr => pr.OrderId)
                    .Distinct()
                    .ToListAsync();

                ViewBag.OrdersWithReadyParts = readyPartOrders;
                ViewData["TechniciansList"] = await _context.Technicians.Where(t => t.IsAvailable).ToListAsync();
            }

            return View(orders);
        }

        // 📌 دالة جديدة: تحديد موعد التركيب للقطعة التي توفرت
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> ScheduleInstallation(int orderId, int technicianId, DateTime scheduledDate, string adminNotes)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order != null)
            {
                order.TechnicianId = technicianId;
                order.ScheduledDate = scheduledDate;

                // إعادة الطلب لحالة (تم التعيين) ليظهر في قائمة الفني كطلب جديد
                order.Status = OrderStatus.Assigned;

                string noteHeader = string.IsNullOrEmpty(order.TechnicianNotes) ? "" : "\n----------------\n";
                order.TechnicianNotes += $"{noteHeader}[تحديث الكول سنتر]: موعد تركيب القطعة يوم {scheduledDate:yyyy/MM/dd hh:mm tt}. ملاحظات: {adminNotes}";

                LogAction("جدولة تركيب نواقص", $"تم تحديد موعد لتركيب القطع للطلب #{orderId} وإسناده للفني");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم تحديد موعد التركيب وتوجيه الطلب للفني بنجاح!";
            }
            return RedirectToAction(nameof(Index));
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
                }

                LogAction("إنشاء طلب جديد", $"تم إنشاء طلب صيانة رقم #{order.OrderId} للعميل {order.CustomerName}");
                await _context.SaveChangesAsync();

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
                var tech = await _context.Technicians.FindAsync(TechnicianId);

                order.TechnicianId = TechnicianId;
                order.Status = OrderStatus.Assigned;
                _context.Update(order);

                LogAction("تعيين فني", $"تم إسناد الطلب #{order.OrderId} إلى الفني ({tech?.Name})");
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

                LogAction("تأكيد موعد", $"تم تأكيد موعد الطلب #{order.OrderId} وإصدار فاتورة مبدئية");
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
                    .ThenInclude(t => t.Inventory)
                        .ThenInclude(i => i.SparePart)
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(up => up.SparePart)
                .FirstOrDefaultAsync(m => m.OrderId == id);

            if (order == null) return NotFound();

            if (User.IsInRole("Technician"))
            {
                if (order.Technician?.Name != User.Identity.Name || order.Status == OrderStatus.Completed || order.Status == OrderStatus.Approved)
                {
                    TempData["ErrorMessage"] = "تم إغلاق هذا الطلب وتسليمه للإدارة للمراجعة، ولا يمكنك التعديل عليه.";
                    return RedirectToAction(nameof(Index));
                }
            }

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

            LogAction("طلب من المخزن", $"طلب حجز قطعة للمخزن للطلب #{orderId} بكمية {quantity}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم إرسال الطلب لأمين المخزن. بانتظار موافقته والصرف.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> ApproveWarehouseRequest(int requestId)
        {
            var request = await _context.OrderPartRequests
                .Include(r => r.Order)
                .Include(r => r.SparePart)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null || request.Status != PartRequestStatus.PendingWarehouse)
                return NotFound();

            if (request.SparePart.MainStockQuantity < request.Quantity)
            {
                TempData["ErrorMessage"] = "الكمية المتوفرة في المخزن لا تكفي لتلبية الطلب!";
                return RedirectToAction(nameof(Details), new { id = request.OrderId });
            }

            request.SparePart.MainStockQuantity -= request.Quantity;

            if (request.Order.TechnicianId.HasValue)
            {
                var techStock = await _context.TechnicianStocks
                    .FirstOrDefaultAsync(ts => ts.TechnicianId == request.Order.TechnicianId.Value && ts.PartId == request.PartId);

                if (techStock != null)
                {
                    techStock.Quantity += request.Quantity;
                    _context.Update(techStock);
                }
                else
                {
                    _context.TechnicianStocks.Add(new TechnicianStock
                    {
                        TechnicianId = request.Order.TechnicianId.Value,
                        PartId = request.PartId.Value,
                        Quantity = request.Quantity
                    });
                }
            }

            request.Status = PartRequestStatus.ReadyForInstallation;
            _context.Update(request);

            LogAction("موافقة وصرف مخزن", $"تم صرف {request.Quantity} من {request.SparePart.Name} لعهدة الفني للطلب #{request.OrderId}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم الموافقة على الطلب وصرف القطعة لعهدة الفني بنجاح.";
            return RedirectToAction(nameof(Details), new { id = request.OrderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Store")]
        public async Task<IActionResult> RejectWarehouseRequest(int requestId)
        {
            var request = await _context.OrderPartRequests
                .Include(r => r.Order)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request != null && request.Status == PartRequestStatus.PendingWarehouse)
            {
                request.Status = PartRequestStatus.Rejected;
                _context.Update(request);

                LogAction("رفض صرف مخزن", $"تم رفض طلب صرف قطعة من المخزن للطلب #{request.OrderId}");
                await _context.SaveChangesAsync();

                TempData["ErrorMessage"] = "تم رفض طلب القطعة من المخزن.";
            }
            return RedirectToAction(nameof(Details), new { id = request?.OrderId });
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

            LogAction("طلب شراء", $"طلب شراء قطعة غير متوفرة ({partName}) للطلب #{orderId}");
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
                if (User.IsInRole("Technician") && (newStatus == OrderStatus.Approved || newStatus == OrderStatus.Returned))
                {
                    return Unauthorized();
                }

                string oldStatusStr = order.Status.ToString();
                order.Status = newStatus;
                order.TechnicianNotes = technicianNotes;
                order.IsFeeApplied = (isFeeApplied == 1);

                decimal partsTotal = order.UsedSpareParts?.Sum(p => p.QuantityUsed * p.SellingPriceAtTime) ?? 0;
                decimal appliedFee = order.IsFeeApplied ? order.EstimatedPrice : 0;

                order.FinalPrice = appliedFee + partsTotal - order.AdvancePayment;

                if (newStatus == OrderStatus.Approved)
                {
                    var finalInvoice = order.Invoices?.FirstOrDefault(i => i.Type == InvoiceType.Final);
                    if (finalInvoice == null)
                    {
                        var invoice = new Invoice
                        {
                            OrderId = order.OrderId,
                            Amount = order.FinalPrice > 0 ? order.FinalPrice : 0,
                            Type = InvoiceType.Final,
                            Status = InvoiceStatus.NotReceived,
                            IssuedAt = DateTime.Now
                        };
                        _context.Invoices.Add(invoice);
                    }
                    else
                    {
                        finalInvoice.Amount = order.FinalPrice > 0 ? order.FinalPrice : 0;
                        _context.Update(finalInvoice);
                    }
                }

                _context.Update(order);

                LogAction("تحديث حالة الطلب", $"تحديث الطلب #{id} من {oldStatusStr} إلى {newStatus}");
                await _context.SaveChangesAsync();

                if (newStatus == OrderStatus.Completed)
                    TempData["SuccessMessage"] = "تم إكمال الطلب وإرساله للإدارة للمراجعة والاعتماد.";
                else if (newStatus == OrderStatus.Returned)
                    TempData["ErrorMessage"] = "تم إرجاع الطلب للفني للتعديل بناءً على ملاحظاتك.";
                else
                    TempData["SuccessMessage"] = "تم تحديث حالة الطلب بنجاح!";
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

                    decimal appliedFee = order.IsFeeApplied ? order.EstimatedPrice : 0;
                    order.FinalPrice = appliedFee + (order.UsedSpareParts.Sum(p => p.QuantityUsed * p.SellingPriceAtTime)) + (part.SellingPrice * quantity) - order.AdvancePayment;
                    _context.Update(order);

                    LogAction("استهلاك عهدة", $"تركيب قطعة ({part.Name}) بكمية ({quantity}) في الطلب #{orderId}");
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

            LogAction("طباعة فاتورة", $"قام المستخدم بعرض/طباعة الفاتورة الخاصة بالطلب #{id}");
            await _context.SaveChangesAsync();

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

            LogAction("طباعة عرض سعر", $"قام المستخدم بإنشاء عرض سعر للطلب #{id}");
            await _context.SaveChangesAsync();

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
                LogAction("تعديل طلب", $"تعديل إداري على بيانات الطلب #{order.OrderId}");
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
                LogAction("حذف طلب", $"حذف الطلب #{order.OrderId} الخاص بالعميل {order.CustomerName} نهائياً");
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

            if ((order.Status == OrderStatus.Completed || order.Status == OrderStatus.Approved) && !User.IsInRole("Admin"))
            {
                TempData["ErrorMessage"] = "مغلق! لا يمكن حذف القطع بعد تسليم الطلب للإدارة للراجعة.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

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

            order.FinalPrice -= (usedPart.QuantityUsed * usedPart.SellingPriceAtTime);
            _context.Update(order);
            _context.UsedSpareParts.Remove(usedPart);

            LogAction("إرجاع قطعة من الفاتورة", $"تم إزالة قطعة من الفاتورة وإعادتها لعهدة الفني للطلب #{orderId}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف القطعة، إرجاعها للعهدة، وتحديث الفاتورة بنجاح.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int orderId, PaymentMethod paymentMethod, IFormFile paymentReceipt)
        {
            var order = await _context.Orders.Include(o => o.Invoices).FirstOrDefaultAsync(o => o.OrderId == orderId);
            if (order == null) return NotFound();

            order.PaymentMethod = paymentMethod;
            order.IsPaid = true;

            if (paymentReceipt != null && paymentReceipt.Length > 0)
            {
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "receipts");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                string uniqueFileName = Guid.NewGuid().ToString() + "_" + paymentReceipt.FileName;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await paymentReceipt.CopyToAsync(fileStream);
                }
                order.PaymentReceiptPath = "/uploads/receipts/" + uniqueFileName;
            }

            if (order.Invoices != null)
            {
                foreach (var invoice in order.Invoices) { invoice.Status = InvoiceStatus.Paid; }
            }

            if (paymentMethod == PaymentMethod.Cash && order.FinalPrice > 0)
            {
                bool alreadyInSafe = await _context.SafeTransactions.AnyAsync(s => s.OrderId == orderId && s.Type == SafeTransactionType.Income);
                if (!alreadyInSafe)
                {
                    var safeTransaction = new SafeTransaction
                    {
                        Amount = order.FinalPrice,
                        Type = SafeTransactionType.Income,
                        Description = $"إيداع كاش (تحصيل فاتورة صيانة للعميل {order.CustomerName})",
                        OrderId = order.OrderId,
                        RecordedBy = User.Identity?.Name ?? "System",
                        Date = DateTime.Now
                    };
                    _context.SafeTransactions.Add(safeTransaction);

                    LogAction("إيداع كاش في الخزنة", $"تم توريد {order.FinalPrice} ريال من الفاتورة #{orderId} إلى الخزنة");
                }
            }

            _context.Update(order);

            LogAction("تحصيل فاتورة", $"تم تحصيل الفاتورة النهائية للطلب #{orderId} بطريقة ({paymentMethod})");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم تأكيد التحصيل بنجاح وتحديث الفواتير والخزنة.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }
    }
}