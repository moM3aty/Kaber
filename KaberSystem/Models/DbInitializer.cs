using System;
using System.Linq;

namespace KaberSystem.Models
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {
            // التأكد من إنشاء قاعدة البيانات
            context.Database.EnsureCreated();

            // التحقق مما إذا كان هناك بيانات مسجلة مسبقاً (لتجنب التكرار)
            if (context.SpareParts.Any())
            {
                return; // قاعدة البيانات تحتوي على بيانات بالفعل
            }
            if (!context.SystemUsers.Any())
            {
                context.SystemUsers.Add(new SystemUser { Username = "admin", Password = "123", Role = "Admin" });
                context.SaveChanges();
            }
            // 1. إضافة قطع الغيار (المخزون العام)
            var parts = new SparePart[]
            {
                new SparePart { Name = "كمبروسر تكييف سامسونج 2 حصان", PurchasePrice = 600m, SellingPrice = 900m, MainStockQuantity = 20 },
                new SparePart { Name = "موتور غسالة LG", PurchasePrice = 450m, SellingPrice = 700m, MainStockQuantity = 15 },
                new SparePart { Name = "فلاتر تكييف سبليت (طقم)", PurchasePrice = 50m, SellingPrice = 120m, MainStockQuantity = 100 },
                new SparePart { Name = "ثرموستات ثلاجة توشيبا", PurchasePrice = 80m, SellingPrice = 150m, MainStockQuantity = 30 },
                new SparePart { Name = "لوحة تحكم إلكترونية (بوردة)", PurchasePrice = 300m, SellingPrice = 550m, MainStockQuantity = 10 }
            };
            context.SpareParts.AddRange(parts);
            context.SaveChanges();

            // 2. إضافة الفنيين
            var techs = new Technician[]
            {
                new Technician { Name = "أحمد محمود", Phone = "0541111111", IsAvailable = true, TotalIncome = 0 },
                new Technician { Name = "خالد عبدالله", Phone = "0542222222", IsAvailable = false, TotalIncome = 0 },
                new Technician { Name = "ياسر عبدالرحمن", Phone = "0543333333", IsAvailable = true, TotalIncome = 0 }
            };
            context.Technicians.AddRange(techs);
            context.SaveChanges();

            // 3. صرف عهدة مبدئية للفنيين
            var techStocks = new TechnicianStock[]
            {
                new TechnicianStock { TechnicianId = techs[0].TechnicianId, PartId = parts[0].PartId, Quantity = 2 },
                new TechnicianStock { TechnicianId = techs[0].TechnicianId, PartId = parts[2].PartId, Quantity = 5 },
                new TechnicianStock { TechnicianId = techs[1].TechnicianId, PartId = parts[1].PartId, Quantity = 1 }
            };
            context.TechnicianStocks.AddRange(techStocks);
            context.SaveChanges();

            // 4. إضافة طلبات صيانة بحالات مختلفة
            var orders = new Order[]
            {
                // طلب جديد
                new Order { CustomerName = "محمد سعد", PhoneNumber = "0500000001", Address = "الدمام - حي الشاطئ", ProblemDescription = "المكيف لا يبرد ويصدر صوتاً مزعجاً", Status = OrderStatus.New, CreatedAt = DateTime.Now.AddDays(-2) },
                // طلب مكتمل
                new Order { CustomerName = "سارة العتيبي", PhoneNumber = "0500000002", Address = "الخبر - حي العليا", ProblemDescription = "الغسالة لا تعصر الماء", Status = OrderStatus.Completed, CreatedAt = DateTime.Now.AddDays(-5), ScheduledDate = DateTime.Now.AddDays(-4), TechnicianId = techs[0].TechnicianId, EstimatedPrice = 150m, FinalPrice = 850m, AdvancePayment = 0m },
                // طلب قيد التنفيذ
                new Order { CustomerName = "عبدالله فهد", PhoneNumber = "0500000003", Address = "الظهران - الدوحة", ProblemDescription = "الثلاجة تفصل بسرعة", Status = OrderStatus.InProgress, CreatedAt = DateTime.Now.AddDays(-1), ScheduledDate = DateTime.Now, TechnicianId = techs[1].TechnicianId, EstimatedPrice = 150m, AdvancePayment = 150m }
            };
            context.Orders.AddRange(orders);
            context.SaveChanges();

            // 5. إضافة فواتير
            var invoices = new Invoice[]
            {
                new Invoice { OrderId = orders[1].OrderId, Amount = 850m, Type = InvoiceType.Final, Status = InvoiceStatus.Paid, IssuedAt = DateTime.Now.AddDays(-4) },
                new Invoice { OrderId = orders[2].OrderId, Amount = 150m, Type = InvoiceType.Advance, Status = InvoiceStatus.Paid, IssuedAt = DateTime.Now.AddDays(-1) }
            };
            context.Invoices.AddRange(invoices);
            context.SaveChanges();

            // 6. إضافة قطع غيار مستهلكة في الطلب المكتمل
            var usedParts = new OrderSparePart[]
            {
                new OrderSparePart { OrderId = orders[1].OrderId, PartId = parts[1].PartId, QuantityUsed = 1, SellingPriceAtTime = 700m }
            };
            context.UsedSpareParts.AddRange(usedParts);
            context.SaveChanges();

            // 7. إضافة بعض المصروفات الوهمية
            var expenses = new Expense[]
            {
                new Expense { Description = "وقود سيارات الصيانة", Amount = 350m, Date = DateTime.Now.AddDays(-1), RecordedBy = "Admin" },
                new Expense { Description = "أدوات تنظيف وفوط للمكيفات", Amount = 120m, Date = DateTime.Now.AddDays(-3), RecordedBy = "Admin" }
            };
            context.Expenses.AddRange(expenses);
            context.SaveChanges();
        }
    }
}