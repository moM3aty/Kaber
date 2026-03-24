using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KaberSystem.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Order> Orders { get; set; }
        public DbSet<Technician> Technicians { get; set; }
        public DbSet<SparePart> SpareParts { get; set; }
        public DbSet<TechnicianStock> TechnicianStocks { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
        public DbSet<OrderSparePart> UsedSpareParts { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<DamagedPart> DamagedParts { get; set; }
        public DbSet<SystemUser> SystemUsers { get; set; }

    }
    public class SystemUser
    {
        [Key]
        public int UserId { get; set; }
        [Required(ErrorMessage = "اسم المستخدم مطلوب")]
        public string Username { get; set; }
        [Required(ErrorMessage = "كلمة المرور مطلوبة")]
        public string Password { get; set; } // يفضل تشفيرها في المشاريع الحقيقية
        [Required(ErrorMessage = "الصلاحية مطلوبة")]
        public string Role { get; set; } // Admin, CallCenter, Technician, etc.
        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }
    public class Order
    {
        [Key]
        public int OrderId { get; set; }
        [Required(ErrorMessage = "اسم العميل مطلوب")]
        public string CustomerName { get; set; }
        [Required(ErrorMessage = "رقم الهاتف مطلوب")]
        public string PhoneNumber { get; set; }
        [Required(ErrorMessage = "العنوان مطلوب")]
        public string Address { get; set; }

        // تم إضافة ? ليصبح الحقل اختيارياً ولا يسبب خطأ
        public string? LocationMapUrl { get; set; }

        [Required(ErrorMessage = "وصف المشكلة مطلوب")]
        public string ProblemDescription { get; set; }

        public OrderStatus Status { get; set; } = OrderStatus.New;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ScheduledDate { get; set; }

        public int? TechnicianId { get; set; }
        [ForeignKey("TechnicianId")]
        public Technician? Technician { get; set; } // إضافة ? لمنع أخطاء الـ Validation

        public decimal EstimatedPrice { get; set; }
        public decimal AdvancePayment { get; set; }
        public decimal FinalPrice { get; set; }

        public ICollection<Invoice>? Invoices { get; set; } // إضافة ?
        public ICollection<OrderSparePart>? UsedSpareParts { get; set; } // إضافة ?
    }

    public enum OrderStatus { New, Assigned, Confirmed, InProgress, Completed, Cancelled }

    public class Invoice
    {
        [Key]
        public int InvoiceId { get; set; }
        public int OrderId { get; set; }
        [ForeignKey("OrderId")]
        public Order? Order { get; set; }

        public decimal Amount { get; set; }
        public InvoiceType Type { get; set; }
        public InvoiceStatus Status { get; set; }
        public DateTime IssuedAt { get; set; } = DateTime.Now;
    }

    public enum InvoiceType { Advance, Final }
    public enum InvoiceStatus { Paid, Rejected, NotReceived }

    public class Technician
    {
        [Key]
        public int TechnicianId { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public string Phone { get; set; }
        public bool IsAvailable { get; set; } = true;
        public decimal TotalIncome { get; set; } = 0;

        public ICollection<Order>? AssignedOrders { get; set; }
        public ICollection<TechnicianStock>? Inventory { get; set; }
    }

    public class SparePart
    {
        [Key]
        public int PartId { get; set; }
        [Required]
        public string Name { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal SellingPrice { get; set; }
        public int MainStockQuantity { get; set; }
    }

    public class TechnicianStock
    {
        [Key]
        public int Id { get; set; }
        public int TechnicianId { get; set; }
        public int PartId { get; set; }
        public int Quantity { get; set; }

        [ForeignKey("TechnicianId")] public Technician? Technician { get; set; }
        [ForeignKey("PartId")] public SparePart? SparePart { get; set; }
    }

    public class OrderSparePart
    {
        [Key]
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int PartId { get; set; }
        public int QuantityUsed { get; set; }
        public decimal SellingPriceAtTime { get; set; }

        [ForeignKey("OrderId")] public Order? Order { get; set; }
        [ForeignKey("PartId")] public SparePart? SparePart { get; set; }
    }

    public class PurchaseOrder
    {
        [Key]
        public int PurchaseId { get; set; }
        [Required]
        public string ItemName { get; set; }
        public int Quantity { get; set; }
        public decimal PurchasePrice { get; set; }
        public DateTime PurchaseDate { get; set; }
        public bool IsReceivedByStore { get; set; } = false;
        public bool IsPricedByManager { get; set; } = false;
    }

    public class Expense
    {
        [Key] public int Id { get; set; }
        [Required]
        public string Description { get; set; }
        public decimal Amount { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
        public string? RecordedBy { get; set; }
    }

    public class DamagedPart
    {
        [Key] public int Id { get; set; }
        public int PartId { get; set; }
        [ForeignKey("PartId")] public SparePart? SparePart { get; set; }
        public int Quantity { get; set; }
        [Required]
        public string Reason { get; set; }
        public DateTime Date { get; set; } = DateTime.Now;
    }
}