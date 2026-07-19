// مسار الملف: Controllers/OrdersController.cs
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using KaberSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using System.IO;
using System.Collections.Generic;

namespace KaberSystem.Controllers
{
    [Authorize(Roles = "Admin,CallCenter,Technician,Orders")]
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
                // 📌 التحديث: إزالة شرط إخفاء الطلبات المعتمدة والملغاة لكي يتمكن الفني من رؤية السجل الخاص به بالكامل
                query = query.Where(o => o.Technician.Name == User.Identity.Name);
            }

            if (!string.IsNullOrEmpty(searchQuery))
            {
                query = query.Where(o => o.CustomerName.Contains(searchQuery) || o.PhoneNumber.Contains(searchQuery) || o.OrderId.ToString() == searchQuery);
            }

            var orders = await query.OrderByDescending(o => o.CreatedAt).ToListAsync();

            if (User.IsInRole("Technician"))
            {
                ViewBag.NewOrdersCount = orders.Count(o => o.Status == OrderStatus.Assigned || o.Status == OrderStatus.Returned);
            }

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

        [HttpGet]
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> SearchPreviousOrders(string query)
        {
            if (string.IsNullOrEmpty(query)) return Json(new { success = false });

            var orders = await _context.Orders
                .Where(o => (o.Status == OrderStatus.Completed || o.Status == OrderStatus.Approved) &&
                            (o.PhoneNumber.Contains(query) || o.CustomerName.Contains(query) || o.OrderId.ToString() == query))
                .Select(o => new {
                    o.OrderId,
                    o.CustomerName,
                    o.PhoneNumber,
                    o.DeviceName,
                    o.Address,
                    Date = o.CreatedAt.ToString("yyyy-MM-dd")
                })
                .OrderByDescending(o => o.OrderId)
                .Take(5)
                .ToListAsync();

            return Json(new { success = true, data = orders });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> CreateWarrantyOrder(int originalOrderId, string problemDescription, DateTime? scheduledDate)
        {
            var originalOrder = await _context.Orders.FindAsync(originalOrderId);
            if (originalOrder == null) return NotFound();

            var newOrder = new Order
            {
                CustomerName = originalOrder.CustomerName,
                PhoneNumber = originalOrder.PhoneNumber,
                Address = originalOrder.Address,
                LocationMapUrl = originalOrder.LocationMapUrl,
                DeviceName = originalOrder.DeviceName,
                ProblemDescription = $"[طلب ضمان للطلب السابق #{originalOrderId}] - {problemDescription}",
                Type = OrderType.Warranty,
                Status = OrderStatus.New,
                CreatedAt = DateTime.Now,
                ScheduledDate = scheduledDate,
                EstimatedPrice = 0,
                AdvancePayment = 0,
                FinalPrice = 0,
                IsFeeApplied = false
            };

            _context.Orders.Add(newOrder);
            LogAction("إنشاء طلب ضمان", $"تم إنشاء طلب زيارة ضمان للعميل {newOrder.CustomerName} مبني على الفاتورة السابقة #{originalOrderId}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"تم فتح طلب ضمان جديد برقم #{newOrder.OrderId} للعميل بنجاح.";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [Authorize(Roles = "Admin,CallCenter")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSchedule(int orderId, DateTime? newScheduledDate)
        {
            var order = await _context.Orders.FindAsync(orderId);
            if (order != null)
            {
                string oldDate = order.ScheduledDate?.ToString("yyyy-MM-dd hh:mm tt") ?? "لم يكن محدد";
                string newDateStr = newScheduledDate?.ToString("yyyy-MM-dd hh:mm tt") ?? "تم الإلغاء وبدون موعد";

                order.ScheduledDate = newScheduledDate;
                _context.Update(order);

                LogAction("تعديل موعد طلب", $"تم تعديل الموعد للطلب #{orderId} من ({oldDate}) إلى ({newDateStr})");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = newScheduledDate.HasValue ? "تم تحديث وتعديل الموعد بنجاح!" : "تم إلغاء الموعد بنجاح.";
            }
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Create()
        {
            ViewData["TechnicianId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(await _context.Technicians.Where(t => t.IsAvailable).ToListAsync(), "TechnicianId", "Name");
            return View();
        }

        [HttpPost]
        [Authorize(Roles = "Admin,CallCenter")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Order order, List<string> DeviceNamesList, List<IFormFile> deviceImages)
        {
            ModelState.Remove("DeviceName");

            if (ModelState.IsValid)
            {
                if (DeviceNamesList != null && DeviceNamesList.Any(d => !string.IsNullOrWhiteSpace(d)))
                {
                    var numberedDevices = DeviceNamesList
                        .Where(d => !string.IsNullOrWhiteSpace(d))
                        .Select((d, i) => $"{i + 1}- {d}")
                        .ToList();

                    order.DeviceName = string.Join(" \n ", numberedDevices);
                }
                else
                {
                    order.DeviceName = "غير محدد";
                }

                order.Status = order.TechnicianId.HasValue ? OrderStatus.Assigned : OrderStatus.New;
                order.CreatedAt = DateTime.Now;

                if (deviceImages != null && deviceImages.Count > 0)
                {
                    var imagePaths = new List<string>();
                    string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "orders");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    foreach (var file in deviceImages)
                    {
                        if (file.Length > 0)
                        {
                            string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                            string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                            using (var fileStream = new FileStream(filePath, FileMode.Create))
                            {
                                await file.CopyToAsync(fileStream);
                            }
                            imagePaths.Add("/uploads/orders/" + uniqueFileName);
                        }
                    }
                    order.DeviceImagePath = string.Join(",", imagePaths);
                }

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

                LogAction("إنشاء طلب جديد", $"تم إنشاء طلب صيانة رقم #{order.OrderId} للعميل {order.CustomerName} يحتوي على أجهزة متعددة");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "تم إنشاء الطلب بنجاح!";
                return RedirectToAction(nameof(Index));
            }
            ViewData["TechnicianId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(await _context.Technicians.Where(t => t.IsAvailable).ToListAsync(), "TechnicianId", "Name", order.TechnicianId);
            return View(order);
        }

        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var order = await _context.Orders.FindAsync(id);
            if (order == null) return NotFound();
            ViewData["TechnicianId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(await _context.Technicians.Where(t => t.IsAvailable || t.TechnicianId == order.TechnicianId).ToListAsync(), "TechnicianId", "Name", order.TechnicianId);
            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Edit(int id, Order order, List<IFormFile> deviceImages)
        {
            if (id != order.OrderId) return NotFound();

            var existingOrder = await _context.Orders.FindAsync(id);
            if (existingOrder == null) return NotFound();

            existingOrder.CustomerName = order.CustomerName;
            existingOrder.PhoneNumber = order.PhoneNumber;
            existingOrder.DeviceName = order.DeviceName;
            existingOrder.ProblemDescription = order.ProblemDescription;
            existingOrder.Address = order.Address;
            existingOrder.LocationMapUrl = order.LocationMapUrl;
            existingOrder.Type = order.Type;
            existingOrder.EstimatedPrice = order.EstimatedPrice;
            existingOrder.TaxAmount = order.TaxAmount; // 📌 التحديث: حفظ الضريبة عند التعديل
            existingOrder.AdvancePayment = order.AdvancePayment;
            existingOrder.ScheduledDate = order.ScheduledDate;

            if (deviceImages != null && deviceImages.Count > 0)
            {
                var imagePaths = new List<string>();

                if (!string.IsNullOrEmpty(existingOrder.DeviceImagePath))
                {
                    imagePaths.AddRange(existingOrder.DeviceImagePath.Split(',', StringSplitOptions.RemoveEmptyEntries));
                }

                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "orders");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                foreach (var file in deviceImages)
                {
                    if (file.Length > 0)
                    {
                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(fileStream);
                        }
                        imagePaths.Add("/uploads/orders/" + uniqueFileName);
                    }
                }

                existingOrder.DeviceImagePath = string.Join(",", imagePaths);
            }

            if (existingOrder.TechnicianId != order.TechnicianId)
            {
                existingOrder.TechnicianId = order.TechnicianId;
                if (order.TechnicianId.HasValue && existingOrder.Status == OrderStatus.New)
                {
                    existingOrder.Status = OrderStatus.Assigned;
                }
            }

            _context.Update(existingOrder);
            LogAction("تعديل طلب", $"تعديل بيانات الطلب #{order.OrderId} للعميل {order.CustomerName} وإضافة صور/تعديل أسعار");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حفظ التعديلات وإضافة الصور للطلب بنجاح!";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,CallCenter")]
        // 📌 التحديث: إضافة taxAmount لجدولة نواقص التركيب
        public async Task<IActionResult> ScheduleInstallation(int orderId, int technicianId, DateTime scheduledDate, decimal additionalInstallFee, decimal taxAmount, string adminNotes)
        {
            var oldOrder = await _context.Orders.FindAsync(orderId);
            if (oldOrder != null)
            {
                oldOrder.Status = OrderStatus.Completed;
                string noteHeader = string.IsNullOrEmpty(oldOrder.TechnicianNotes) ? "" : "\n----------------\n";
                oldOrder.TechnicianNotes += $"{noteHeader}[النظام المحاسبي]: تم إنهاء هذا الطلب (زيارة فحص)، وتم إنشاء طلب (تركيب نواقص) جديد منفصل للموعد الجديد.";
                _context.Update(oldOrder);

                var newOrder = new Order
                {
                    CustomerName = oldOrder.CustomerName,
                    PhoneNumber = oldOrder.PhoneNumber,
                    Address = oldOrder.Address,
                    LocationMapUrl = oldOrder.LocationMapUrl,
                    DeviceName = oldOrder.DeviceName,
                    ProblemDescription = $"[متابعة تركيب نواقص للطلب القديم #{oldOrder.OrderId}]\nملاحظات الإدارة: {adminNotes}",
                    Type = OrderType.Maintenance,
                    Status = OrderStatus.Assigned,
                    CreatedAt = DateTime.Now,
                    ScheduledDate = scheduledDate,
                    TechnicianId = technicianId,
                    EstimatedPrice = additionalInstallFee,
                    TaxAmount = taxAmount, // 📌 التحديث: حفظ الضريبة
                    IsFeeApplied = additionalInstallFee > 0,
                    AdvancePayment = 0,
                    FinalPrice = additionalInstallFee + taxAmount // إضافة الضريبة للإجمالي
                };

                _context.Orders.Add(newOrder);
                await _context.SaveChangesAsync();

                if (additionalInstallFee > 0)
                {
                    var invoice = new Invoice
                    {
                        OrderId = newOrder.OrderId,
                        Amount = additionalInstallFee,
                        Type = InvoiceType.Advance,
                        Status = InvoiceStatus.NotReceived,
                        IssuedAt = DateTime.Now
                    };
                    _context.Invoices.Add(invoice);
                }

                var pendingPartRequests = await _context.OrderPartRequests
                    .Where(pr => pr.OrderId == orderId && pr.Status == PartRequestStatus.ReadyForInstallation)
                    .ToListAsync();

                foreach (var req in pendingPartRequests)
                {
                    req.OrderId = newOrder.OrderId;
                    req.Status = PartRequestStatus.Installed;
                    _context.Update(req);
                }

                LogAction("جدولة زيارة تركيب منفصلة", $"تم إغلاق الطلب #{orderId} وإنشاء طلب جديد #{newOrder.OrderId} لزيارة التركيب بأجرة {additionalInstallFee} ريال ونقل النواقص إليه.");
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"تم إغلاق طلب الفحص، وإنشاء طلب جديد برقم #{newOrder.OrderId} لزيارة التركيب بنجاح!";
            }
            return RedirectToAction(nameof(Index));
        }

        [Authorize(Roles = "Admin,CallCenter")]
        public async Task<IActionResult> Assign(int? id)
        {
            if (id == null) return NotFound();

            var order = await _context.Orders.FindAsync(id);
            if (order == null) return NotFound();

            ViewData["TechnicianId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(_context.Technicians.Where(t => t.IsAvailable), "TechnicianId", "Name");
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

            ViewData["TechnicianId"] = new Microsoft.AspNetCore.Mvc.Rendering.SelectList(_context.Technicians, "TechnicianId", "Name", order.TechnicianId);
            return View(order);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,CallCenter")]
        [ValidateAntiForgeryToken]
        // 📌 التحديث: إضافة حقل الضريبة أثناء تأكيد الموعد
        public async Task<IActionResult> Confirm(int id, DateTime scheduledDate, decimal estimatedPrice, decimal advancePayment, decimal taxAmount)
        {
            var order = await _context.Orders.FindAsync(id);
            if (order != null)
            {
                order.ScheduledDate = scheduledDate;
                order.EstimatedPrice = estimatedPrice;
                order.AdvancePayment = advancePayment;
                order.TaxAmount = taxAmount; // 📌 التحديث: حفظ الضريبة
                order.Status = OrderStatus.Confirmed;
                order.FinalPrice = estimatedPrice + taxAmount; // الإجمالي المبدئي

                if (advancePayment > 0)
                {
                    _context.SafeTransactions.Add(new SafeTransaction
                    {
                        Amount = advancePayment,
                        Type = SafeTransactionType.Income,
                        TargetSafe = SafeType.General,
                        Description = $"دفعة مقدمة (عربون) لطلب #{order.OrderId} للعميل {order.CustomerName}",
                        OrderId = order.OrderId,
                        RecordedBy = User.Identity?.Name ?? "System",
                        Date = DateTime.Now
                    });
                }

                var invoice = new Invoice
                {
                    OrderId = order.OrderId,
                    Amount = estimatedPrice,
                    Type = InvoiceType.Advance,
                    Status = advancePayment > 0 ? InvoiceStatus.Paid : InvoiceStatus.NotReceived,
                    IssuedAt = DateTime.Now
                };

                _context.Invoices.Add(invoice);
                _context.Update(order);

                LogAction("تأكيد موعد", $"تم تأكيد الطلب #{order.OrderId}، واستلام دفعة مقدمة {advancePayment} ريال");
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
                if (order.Technician?.Name != User.Identity.Name)
                {
                    TempData["ErrorMessage"] = "عفواً، لا تملك الصلاحية لعرض تفاصيل هذا الطلب لأنه مسند لفني آخر.";
                    return RedirectToAction(nameof(Index));
                }

                // 📌 التحديث: رسالة توضيحية للفني داخل شاشة الطلب بأنه للمراجعة فقط ولا يمكن تعديله
                if (order.Status == OrderStatus.Approved || order.Status == OrderStatus.Cancelled)
                {
                    TempData["InfoMessage"] = "هذا الطلب منتهي ومغلق بالكامل، يمكنك مراجعته والاطلاع عليه فقط.";
                }
            }

            ViewBag.PartRequests = await _context.OrderPartRequests
                .Include(pr => pr.SparePart)
                .Where(pr => pr.OrderId == id)
                .OrderByDescending(pr => pr.RequestDate)
                .ToListAsync();

            ViewData["AvailableParts"] = await _context.SpareParts.Where(p => p.MainStockQuantity > 0).ToListAsync();

            var orderTransactions = await _context.SafeTransactions
                .Where(t => t.OrderId == id && t.Type == SafeTransactionType.Income)
                .ToListAsync();

            ViewBag.TotalPaid = orderTransactions.Sum(t => t.Amount);

            return View(order);
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
                // 📌 التحديث: حماية خلفية قوية لمنع الفني من تعديل أي شيء إذا كان الطلب معتمداً أو ملغياً
                if (User.IsInRole("Technician"))
                {
                    if (order.Status == OrderStatus.Approved || order.Status == OrderStatus.Cancelled)
                    {
                        TempData["ErrorMessage"] = "عفواً، لا يمكنك التعديل على طلب تم إغلاقه واعتماده.";
                        return RedirectToAction(nameof(Details), new { id = id });
                    }

                    if (newStatus == OrderStatus.Approved || newStatus == OrderStatus.Returned)
                    {
                        return Unauthorized();
                    }
                }

                string oldStatusStr = order.Status.ToString();
                order.Status = newStatus;
                order.TechnicianNotes = technicianNotes;

                if (isFeeApplied == 1)
                {
                    order.IsFeeApplied = true;
                    order.IsBlacklisted = false;
                }
                else if (isFeeApplied == 0)
                {
                    order.IsFeeApplied = false;
                    order.IsBlacklisted = false;
                }
                else if (isFeeApplied == -1)
                {
                    order.IsFeeApplied = false;
                    order.IsBlacklisted = true;
                    order.TechnicianNotes += "\n[النظام]: تم وضع العميل في القائمة السوداء (رفض دفع الرسوم).";
                }

                decimal partsTotal = order.UsedSpareParts?.Sum(p => p.QuantityUsed * p.SellingPriceAtTime) ?? 0;
                decimal appliedFee = order.IsFeeApplied ? order.EstimatedPrice : 0;

                // 📌 التحديث: احتساب الضريبة المحفوظة من قبل ضمن الفاتورة النهائية
                order.FinalPrice = appliedFee + partsTotal + order.TaxAmount;

                if (newStatus == OrderStatus.Approved)
                {
                    var finalInvoice = order.Invoices?.FirstOrDefault(i => i.Type == InvoiceType.Final);
                    if (finalInvoice == null)
                    {
                        _context.Invoices.Add(new Invoice
                        {
                            OrderId = order.OrderId,
                            Amount = order.FinalPrice,
                            Type = InvoiceType.Final,
                            Status = order.IsPaid ? InvoiceStatus.Paid : InvoiceStatus.NotReceived,
                            IssuedAt = DateTime.Now
                        });
                    }
                    else
                    {
                        finalInvoice.Amount = order.FinalPrice;
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
                .Include(o => o.UsedSpareParts)
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
                    if (techStock.Quantity == 0) _context.TechnicianStocks.Remove(techStock);
                    else _context.Update(techStock);

                    _context.UsedSpareParts.Add(new OrderSparePart
                    {
                        OrderId = orderId,
                        PartId = partId,
                        QuantityUsed = quantity,
                        SellingPriceAtTime = part.SellingPrice
                    });

                    decimal appliedFee = order.IsFeeApplied ? order.EstimatedPrice : 0;
                    decimal currentPartsTotal = order.UsedSpareParts.Sum(p => p.QuantityUsed * p.SellingPriceAtTime) + (quantity * part.SellingPrice);

                    // 📌 التحديث: إضافة الضريبة
                    order.FinalPrice = appliedFee + currentPartsTotal + order.TaxAmount;
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
                TempData["ErrorMessage"] = "مغلق! لا يمكن حذف القطع بعد تسليم الطلب للإدارة.";
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

            decimal removedValue = usedPart.QuantityUsed * usedPart.SellingPriceAtTime;
            order.FinalPrice -= removedValue;
            if (order.FinalPrice < 0) order.FinalPrice = 0;

            _context.Update(order);
            _context.UsedSpareParts.Remove(usedPart);

            LogAction("إرجاع قطعة من الفاتورة", $"تم إزالة قطعة من الفاتورة وإعادتها لعهدة الفني للطلب #{orderId}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم حذف القطعة، وإرجاعها للعهدة، وتحديث الفاتورة بنجاح.";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessPayment(int orderId, decimal cashAmount, decimal posAmount, decimal bankTransferAmount, IFormFile paymentReceipt)
        {
            var order = await _context.Orders
                .Include(o => o.Invoices)
                .Include(o => o.UsedSpareParts)
                    .ThenInclude(p => p.SparePart)
                .FirstOrDefaultAsync(o => o.OrderId == orderId);

            if (order == null) return NotFound();

            decimal totalPaidNow = cashAmount + posAmount + bankTransferAmount;
            if (totalPaidNow <= 0)
            {
                TempData["ErrorMessage"] = "الرجاء إدخال مبلغ صحيح للتحصيل.";
                return RedirectToAction(nameof(Details), new { id = orderId });
            }

            if (paymentReceipt != null && paymentReceipt.Length > 0)
            {
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "receipts");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
                string uniqueFileName = Guid.NewGuid().ToString() + "_" + paymentReceipt.FileName;
                string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var fileStream = new FileStream(filePath, FileMode.Create)) { await paymentReceipt.CopyToAsync(fileStream); }
                order.PaymentReceiptPath = "/uploads/receipts/" + uniqueFileName;
            }

            decimal totalPartsCost = order.UsedSpareParts?.Sum(p => p.QuantityUsed * (p.SparePart?.PurchasePrice ?? 0)) ?? 0;

            var previousTransactions = await _context.SafeTransactions.Where(t => t.OrderId == orderId && t.Type == SafeTransactionType.Income).ToListAsync();
            decimal previouslyPaidCost = previousTransactions.Where(t => t.TargetSafe == SafeType.Purchasing).Sum(t => t.Amount);

            decimal remainingCostToCover = totalPartsCost - previouslyPaidCost;
            if (remainingCostToCover < 0) remainingCostToCover = 0;

            void ProcessPaymentPortion(decimal amount, PaymentMethod method)
            {
                if (amount <= 0) return;

                if (remainingCostToCover > 0)
                {
                    if (amount <= remainingCostToCover)
                    {
                        _context.SafeTransactions.Add(new SafeTransaction { Amount = amount, Type = SafeTransactionType.Income, TargetSafe = SafeType.Purchasing, PaymentMethod = method, Description = $"استرداد رأس مال قطع للطلب #{order.OrderId}", OrderId = order.OrderId, RecordedBy = User.Identity?.Name ?? "System", Date = DateTime.Now });
                        remainingCostToCover -= amount;
                    }
                    else
                    {
                        _context.SafeTransactions.Add(new SafeTransaction { Amount = remainingCostToCover, Type = SafeTransactionType.Income, TargetSafe = SafeType.Purchasing, PaymentMethod = method, Description = $"استرداد رأس مال قطع للطلب #{order.OrderId}", OrderId = order.OrderId, RecordedBy = User.Identity?.Name ?? "System", Date = DateTime.Now });
                        decimal profitPart = amount - remainingCostToCover;
                        _context.SafeTransactions.Add(new SafeTransaction { Amount = profitPart, Type = SafeTransactionType.Income, TargetSafe = SafeType.General, PaymentMethod = method, Description = $"تحصيل أجور وأرباح للطلب #{order.OrderId}", OrderId = order.OrderId, RecordedBy = User.Identity?.Name ?? "System", Date = DateTime.Now });
                        remainingCostToCover = 0;
                    }
                }
                else
                {
                    _context.SafeTransactions.Add(new SafeTransaction { Amount = amount, Type = SafeTransactionType.Income, TargetSafe = SafeType.General, PaymentMethod = method, Description = $"تحصيل أجور وأرباح للطلب #{order.OrderId}", OrderId = order.OrderId, RecordedBy = User.Identity?.Name ?? "System", Date = DateTime.Now });
                }
            }

            ProcessPaymentPortion(cashAmount, PaymentMethod.Cash);
            ProcessPaymentPortion(posAmount, PaymentMethod.POS);
            ProcessPaymentPortion(bankTransferAmount, PaymentMethod.BankTransfer);

            if (cashAmount >= posAmount && cashAmount >= bankTransferAmount) order.PaymentMethod = PaymentMethod.Cash;
            else if (posAmount >= cashAmount && posAmount >= bankTransferAmount) order.PaymentMethod = PaymentMethod.POS;
            else order.PaymentMethod = PaymentMethod.BankTransfer;

            decimal totalPaidAllTime = previousTransactions.Sum(t => t.Amount) + totalPaidNow;
            if (totalPaidAllTime >= order.FinalPrice)
            {
                order.IsPaid = true;
                if (order.Invoices != null)
                {
                    foreach (var invoice in order.Invoices) { invoice.Status = InvoiceStatus.Paid; }
                }
            }

            LogAction("تحصيل مجزأ", $"تم تحصيل ({totalPaidNow} ريال) للطلب #{orderId} مجزأة (كاش:{cashAmount}, شبكة:{posAmount}, تحويل:{bankTransferAmount})");

            _context.Update(order);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم تسجيل المدفوعات وتوجيهها للخزنات بنجاح.";
            return RedirectToAction(nameof(Details), new { id = orderId });
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
        public async Task<IActionResult> RequestPurchase(int orderId, string partName, string deviceModel, int quantity, bool isCommon, List<IFormFile> partImages)
        {
            string finalImagePaths = null;

            if (partImages != null && partImages.Count > 0)
            {
                var imagePathsList = new List<string>();
                string uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "parts");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                foreach (var file in partImages)
                {
                    if (file.Length > 0)
                    {
                        string uniqueFileName = Guid.NewGuid().ToString() + "_" + file.FileName;
                        string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                        using (var fileStream = new FileStream(filePath, FileMode.Create))
                        {
                            await file.CopyToAsync(fileStream);
                        }
                        imagePathsList.Add("/uploads/parts/" + uniqueFileName);
                    }
                }
                finalImagePaths = string.Join(",", imagePathsList);
            }

            var request = new OrderPartRequest
            {
                OrderId = orderId,
                RequestType = PartRequestType.PurchaseNew,
                NewPartName = partName,
                DeviceModel = deviceModel,
                IsCommonRequest = isCommon,
                Quantity = quantity,
                ImagePath = finalImagePaths,
                Status = PartRequestStatus.PendingPurchasing
            };

            _context.OrderPartRequests.Add(request);

            LogAction("طلب شراء", $"طلب شراء قطعة غير متوفرة ({partName}) للطلب #{orderId}");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "تم إرسال طلب الشراء. سيتم تكويد القطعة وربطها بالموديل عند الشراء.";
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
        public async Task<IActionResult> Delete(int id)
        {
            var order = await _context.Orders
                .Include(o => o.Invoices)
                .Include(o => o.UsedSpareParts)
                .FirstOrDefaultAsync(o => o.OrderId == id);

            if (order != null)
            {
                var safeTransactions = await _context.SafeTransactions.Where(s => s.OrderId == id).ToListAsync();
                if (safeTransactions.Any())
                {
                    _context.SafeTransactions.RemoveRange(safeTransactions);
                }

                var partRequests = await _context.OrderPartRequests.Where(pr => pr.OrderId == id).ToListAsync();
                if (partRequests.Any())
                {
                    _context.OrderPartRequests.RemoveRange(partRequests);
                }

                if (order.UsedSpareParts != null && order.UsedSpareParts.Any())
                {
                    foreach (var usedPart in order.UsedSpareParts)
                    {
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
                    }
                    _context.UsedSpareParts.RemoveRange(order.UsedSpareParts);
                }

                if (order.Invoices != null && order.Invoices.Any())
                {
                    _context.Invoices.RemoveRange(order.Invoices);
                }

                _context.Orders.Remove(order);
                LogAction("حذف طلب", $"حذف الطلب #{order.OrderId} الخاص بالعميل {order.CustomerName} نهائياً وكل الحركات المرتبطة به");
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "تم حذف الطلب نهائياً من النظام مع إرجاع القطع للعهدة وتنظيف سجلاته المالية.";
            }
            return RedirectToAction(nameof(Index));
        }
    }
}