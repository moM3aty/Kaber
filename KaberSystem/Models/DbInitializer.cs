using System;
using System.Linq;

namespace KaberSystem.Models
{
    public static class DbInitializer
    {
        public static void Initialize(ApplicationDbContext context)
        {

            // التحقق مما إذا كان هناك بيانات مسجلة مسبقاً (لتجنب التكرار)
            if (context.SpareParts.Any())
            {
                return; // قاعدة البيانات تحتوي على بيانات بالفعل
            }

            // 0. إضافة مستخدمين النظام بجميع الصلاحيات لتجربة الدخول
            if (!context.SystemUsers.Any())
            {
                var users = new SystemUser[]
                {
                    new SystemUser { Username = "admin", Password = "123", Role = "Admin" },
                    new SystemUser { Username = "callcenter", Password = "123", Role = "CallCenter" },
                    new SystemUser { Username = "store", Password = "123", Role = "Store" },
                    new SystemUser { Username = "acc", Password = "123", Role = "Accounting" },
                    new SystemUser { Username = "purchase", Password = "123", Role = "PurchasingManager" }
                };
                context.SystemUsers.AddRange(users);
                context.SaveChanges();
            }

            // 1. إضافة قطع الغيار (المخزون العام) مع بيانات الموردين
            var parts = new SparePart[]
            {
                new SparePart { Name = "كمبروسر تكييف سامسونج 2 حصان", PurchasePrice = 600m, SellingPrice = 900m, MainStockQuantity = 20, SupplierName = "شركة سامسونج الكورية", SupplierPhone = "0501112223" },
                new SparePart { Name = "موتور غسالة LG", PurchasePrice = 450m, SellingPrice = 700m, MainStockQuantity = 15, SupplierName = "مؤسسة ال جي المعتمدة", SupplierPhone = "0509998887" },
                new SparePart { Name = "فلاتر تكييف سبليت (طقم)", PurchasePrice = 50m, SellingPrice = 120m, MainStockQuantity = 100, SupplierName = "مورد فلاتر محلي" },
                new SparePart { Name = "ثرموستات ثلاجة توشيبا", PurchasePrice = 80m, SellingPrice = 150m, MainStockQuantity = 30 },
                new SparePart { Name = "لوحة تحكم إلكترونية (بوردة)", PurchasePrice = 300m, SellingPrice = 550m, MainStockQuantity = 10 },
                new SparePart { Name = "غاز فريون أمريكي أسطوانة", PurchasePrice = 200m, SellingPrice = 350m, MainStockQuantity = 5 }
            };
            context.SpareParts.AddRange(parts);
            context.SaveChanges();

            // 2. إضافة الفنيين
            var techs = new Technician[]
            {
                new Technician { Name = "أحمد محمود", Phone = "0541111111", IsAvailable = true, TotalIncome = 0 },
                new Technician { Name = "خالد عبدالله", Phone = "0542222222", IsAvailable = false, TotalIncome = 0 },
                new Technician { Name = "ياسر عبدالرحمن", Phone = "0543333333", IsAvailable = true, TotalIncome = 0 },
                new Technician { Name = "عمر الجاسم", Phone = "0544444444", IsAvailable = true, TotalIncome = 0 }
            };
            context.Technicians.AddRange(techs);
            context.SaveChanges();

            // 3. صرف عهدة مبدئية للفنيين
            var techStocks = new TechnicianStock[]
            {
                new TechnicianStock { TechnicianId = techs[0].TechnicianId, PartId = parts[0].PartId, Quantity = 2 },
                new TechnicianStock { TechnicianId = techs[0].TechnicianId, PartId = parts[2].PartId, Quantity = 5 },
                new TechnicianStock { TechnicianId = techs[0].TechnicianId, PartId = parts[5].PartId, Quantity = 1 },
                new TechnicianStock { TechnicianId = techs[1].TechnicianId, PartId = parts[1].PartId, Quantity = 1 },
                new TechnicianStock { TechnicianId = techs[2].TechnicianId, PartId = parts[4].PartId, Quantity = 2 }
            };
            context.TechnicianStocks.AddRange(techStocks);
            context.SaveChanges();

            // 4. إضافة طلبات صيانة بحالات مختلفة (مع التأكد من إضافة DeviceName الإجباري)
            var orders = new Order[]
            {
                // طلب جديد
                new Order { CustomerName = "محمد سعد", PhoneNumber = "0500000001", Address = "الدمام - حي الشاطئ", DeviceName = "مكيف سبليت جري", ProblemDescription = "المكيف لا يبرد ويصدر صوتاً مزعجاً", Status = OrderStatus.New, CreatedAt = DateTime.Now.AddDays(-1) },
                
                // طلب جديد آخر
                new Order { CustomerName = "نورة الفيصل", PhoneNumber = "0500000099", Address = "الخبر - العقربية", DeviceName = "فرن كهربائي بوش", ProblemDescription = "الفرن لا يسخن من الأسفل", Status = OrderStatus.New, CreatedAt = DateTime.Now },

                // طلب مكتمل (ورسومه مطبقة)
                new Order { CustomerName = "سارة العتيبي", PhoneNumber = "0500000002", Address = "الخبر - حي العليا", DeviceName = "غسالة أوتوماتيك LG", ProblemDescription = "الغسالة لا تعصر الماء وتصدر صوت احتكاك", Status = OrderStatus.Completed, CreatedAt = DateTime.Now.AddDays(-5), ScheduledDate = DateTime.Now.AddDays(-4), TechnicianId = techs[0].TechnicianId, EstimatedPrice = 150m, IsFeeApplied = true, FinalPrice = 850m, AdvancePayment = 0m, TechnicianNotes = "تم الفحص وتغيير الموتور التالف بآخر جديد، الغسالة تعمل بكفاءة الآن." },
                
                // طلب مكتمل (وتم إسقاط الرسوم IsFeeApplied = false)
                new Order { CustomerName = "فيصل الدوسري", PhoneNumber = "0500000005", Address = "الدمام - حي الفيصلية", DeviceName = "مكيف شباك", ProblemDescription = "تنظيف دوري", Status = OrderStatus.Completed, CreatedAt = DateTime.Now.AddDays(-3), ScheduledDate = DateTime.Now.AddDays(-2), TechnicianId = techs[0].TechnicianId, EstimatedPrice = 100m, IsFeeApplied = false, FinalPrice = 120m, AdvancePayment = 0m, TechnicianNotes = "تم تنظيف المكيف بالكامل وتغيير طقم الفلاتر. تم إعفاء العميل من رسوم الزيارة." },

                // طلب قيد التنفيذ
                new Order { CustomerName = "عبدالله فهد", PhoneNumber = "0500000003", Address = "الظهران - الدوحة", DeviceName = "ثلاجة توشيبا", ProblemDescription = "الثلاجة تفصل بسرعة ولا يوجد تجميد في الفريزر", Status = OrderStatus.InProgress, CreatedAt = DateTime.Now.AddDays(-1), ScheduledDate = DateTime.Now, TechnicianId = techs[1].TechnicianId, EstimatedPrice = 150m, AdvancePayment = 150m },

                // طلب تم تعيينه ولم يبدأ
                new Order { CustomerName = "مريم خالد", PhoneNumber = "0500000004", Address = "الدمام - حي النور", DeviceName = "برادة مياه", ProblemDescription = "تسريب مياه من الأسفل", Status = OrderStatus.Assigned, CreatedAt = DateTime.Now.AddDays(-2), TechnicianId = techs[2].TechnicianId }
            };
            context.Orders.AddRange(orders);
            context.SaveChanges();

            // 5. إضافة فواتير (للحسابات)
            var invoices = new Invoice[]
            {
                new Invoice { OrderId = orders[2].OrderId, Amount = 850m, Type = InvoiceType.Final, Status = InvoiceStatus.Paid, IssuedAt = DateTime.Now.AddDays(-4) },
                new Invoice { OrderId = orders[3].OrderId, Amount = 120m, Type = InvoiceType.Final, Status = InvoiceStatus.NotReceived, IssuedAt = DateTime.Now.AddDays(-2) }, // فاتورة لم تُحصل بعد
                new Invoice { OrderId = orders[4].OrderId, Amount = 150m, Type = InvoiceType.Advance, Status = InvoiceStatus.Paid, IssuedAt = DateTime.Now.AddDays(-1) }
            };
            context.Invoices.AddRange(invoices);
            context.SaveChanges();

            // 6. إضافة قطع غيار مستهلكة في الطلبات المكتملة
            var usedParts = new OrderSparePart[]
            {
                // للموتور في الطلب 3
                new OrderSparePart { OrderId = orders[2].OrderId, PartId = parts[1].PartId, QuantityUsed = 1, SellingPriceAtTime = 700m },
                // للفلاتر في الطلب 4
                new OrderSparePart { OrderId = orders[3].OrderId, PartId = parts[2].PartId, QuantityUsed = 1, SellingPriceAtTime = 120m }
            };
            context.UsedSpareParts.AddRange(usedParts);
            context.SaveChanges();

            // 7. إضافة مصروفات (عامة وسلف للفنيين)
            var expenses = new Expense[]
            {
                // مصروف عام للشركة
                new Expense { Description = "أدوات تنظيف وفوط للمكيفات", Amount = 120m, Date = DateTime.Now.AddDays(-3), RecordedBy = "Admin", TechnicianId = null },
                new Expense { Description = "فاتورة إنترنت للمكتب", Amount = 200m, Date = DateTime.Now.AddDays(-5), RecordedBy = "Admin", TechnicianId = null },
                
                // مصروف مسجل كـ(سلفة أو خصم) على الفني أحمد محمود
                new Expense { Description = "وقود سيارة الصيانة الخاصة بالفني", Amount = 100m, Date = DateTime.Now.AddDays(-1), RecordedBy = "Admin", TechnicianId = techs[0].TechnicianId },
                // مصروف مسجل على فني آخر
                new Expense { Description = "سلفة شخصية من الصندوق", Amount = 300m, Date = DateTime.Now.AddDays(-2), RecordedBy = "Admin", TechnicianId = techs[1].TechnicianId }
            };
            context.Expenses.AddRange(expenses);
            context.SaveChanges();

            // 8. إضافة بيانات لقسم المشتريات
            var purchases = new PurchaseOrder[]
            {
                // طلب قيد الانتظار (في الطريق)
                new PurchaseOrder { ItemName = "كمبروسر تكييف ال جي", Quantity = 10, PurchasePrice = 500m, PurchaseDate = DateTime.Now, IsReceivedByStore = false, SupplierName = "وكالة ال جي", SupplierPhone = "0555555555" },
                // طلب تم استلامه من المخزن ولكن لم يُسعر من الإدارة
                new PurchaseOrder { ItemName = "مراوح شفط مركزية", Quantity = 5, PurchasePrice = 1200m, PurchaseDate = DateTime.Now.AddDays(-2), IsReceivedByStore = true, IsPricedByManager = false, SupplierName = "المصنع الوطني للتبريد" }
            };
            context.PurchaseOrders.AddRange(purchases);
            context.SaveChanges();

            // 9. إضافة توالف للمخزن
            var damages = new DamagedPart[]
            {
                new DamagedPart { PartId = parts[3].PartId, Quantity = 2, Reason = "كسر في البلاستيك الخارجي أثناء النقل للمخزن", Date = DateTime.Now.AddDays(-4) }
            };
            context.DamagedParts.AddRange(damages);
            context.SaveChanges();
        }
    }
}