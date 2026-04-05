// مسار الملف: Models/Models.cs
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
        public DbSet<SafeTransaction> SafeTransactions { get; set; }
        public DbSet<OrderPartRequest> OrderPartRequests { get; set; }
        public DbSet<SystemLog> SystemLogs { get; set; }
        public DbSet<LeaveRequest> LeaveRequests { get; set; }
        public DbSet<PayrollSchedule> PayrollSchedules { get; set; }
        public DbSet<Partner> Partners { get; set; }

        // 📌 جداول الـ HR الجديدة
        public DbSet<AttendanceRecord> AttendanceRecords { get; set; }
        public DbSet<PayrollRecord> PayrollRecords { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            configurationBuilder.Properties<decimal>().HaveColumnType("decimal(18,2)");
        }
    }

    public enum PaymentMethod { None, Cash, BankTransfer, Prepaid, POS }
    public enum SafeTransactionType { Income, DepositToBank }
    public enum SafeType { General, Purchasing }

    public class SystemUser
    {
        [Key]
        public int UserId { get; set; }
        [Required(ErrorMessage = "اسم المستخدم مطلوب")]
        public string Username { get; set; }
        [Required(ErrorMessage = "كلمة المرور مطلوبة")]
        public string Password { get; set; }
        [Required(ErrorMessage = "الصلاحية مطلوبة")]
        public string Role { get; set; }

        public string? Permissions { get; set; }

        // 📌 الراتب الأساسي للموظف
        public decimal BaseSalary { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

    public class SystemLog
    {
        [Key] public int Id { get; set; }
        public string Username { get; set; }
        public string ActionType { get; set; }
        public string Details { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class Order
    { /* الكود الحالي للطلب كما هو لتوفير المساحة */
        [Key] public int OrderId { get; set; }
        [Required] public string CustomerName { get; set; }
        [Required] public string PhoneNumber { get; set; }
        [Required] public string Address { get; set; }
        public string? LocationMapUrl { get; set; }
        [Required] public string DeviceName { get; set; }
        [Required] public string ProblemDescription { get; set; }
        public string? TechnicianNotes { get; set; }
        public bool IsFeeApplied { get; set; } = true;
        public OrderType Type { get; set; } = OrderType.Maintenance;
        public OrderStatus Status { get; set; } = OrderStatus.New;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? ScheduledDate { get; set; }
        public int? TechnicianId { get; set; }
        public string? DeviceImagePath { get; set; }
        [ForeignKey("TechnicianId")] public Technician? Technician { get; set; }
        public decimal EstimatedPrice { get; set; }
        public decimal AdvancePayment { get; set; }
        public decimal FinalPrice { get; set; }
        public ICollection<Invoice>? Invoices { get; set; }
        public ICollection<OrderSparePart>? UsedSpareParts { get; set; }
        public bool IsPaid { get; set; } = false;
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.None;
        public string? PaymentReceiptPath { get; set; }
    }
    public enum OrderType { Maintenance, NewInstallation, Warranty, ACWashing }
    public enum OrderStatus { New, Assigned, Confirmed, InProgress, Completed, Approved, Returned, Cancelled }

    public class SafeTransaction
    {
        [Key] public int Id { get; set; }
        public decimal Amount { get; set; }
        public SafeTransactionType Type { get; set; }
        public SafeType TargetSafe { get; set; } = SafeType.General;
        public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;
        public DateTime Date { get; set; } = DateTime.Now;
        public string Description { get; set; }
        public string RecordedBy { get; set; }
        public int? OrderId { get; set; }
        [ForeignKey("OrderId")] public Order? Order { get; set; }
    }

    public class Invoice { [Key] public int InvoiceId { get; set; } public int OrderId { get; set; } [ForeignKey("OrderId")] public Order? Order { get; set; } public decimal Amount { get; set; } public InvoiceType Type { get; set; } public InvoiceStatus Status { get; set; } public DateTime IssuedAt { get; set; } = DateTime.Now; }
    public enum InvoiceType { Advance, Final }
    public enum InvoiceStatus { Paid, Rejected, NotReceived }

    public class Technician { [Key] public int TechnicianId { get; set; } [Required] public string Name { get; set; } [Required] public string Phone { get; set; } public bool IsAvailable { get; set; } = true; public decimal TotalIncome { get; set; } = 0; public ICollection<Order>? AssignedOrders { get; set; } public ICollection<TechnicianStock>? Inventory { get; set; } public ICollection<Expense>? Expenses { get; set; } }

    public class SparePart { [Key] public int PartId { get; set; } [Required] public string PartCode { get; set; } [Required] public string Name { get; set; } public bool IsCommon { get; set; } = true; public string? TargetModel { get; set; } public decimal PurchasePrice { get; set; } public decimal SellingPrice { get; set; } public int MainStockQuantity { get; set; } public string? SupplierName { get; set; } public string? SupplierPhone { get; set; } public string? SupplierLocation { get; set; } public string? Barcode { get; set; } }

    public class OrderPartRequest { [Key] public int Id { get; set; } public int OrderId { get; set; } [ForeignKey("OrderId")] public Order? Order { get; set; } public PartRequestType RequestType { get; set; } public int? PartId { get; set; } [ForeignKey("PartId")] public SparePart? SparePart { get; set; } public string? NewPartName { get; set; } public string? DeviceModel { get; set; } public bool IsCommonRequest { get; set; } public string? ImagePath { get; set; } public int Quantity { get; set; } public PartRequestStatus Status { get; set; } public DateTime RequestDate { get; set; } = DateTime.Now; }
    public enum PartRequestStatus { PendingWarehouse, PendingPurchasing, PurchasedWaitingStore, ReadyForInstallation, Installed, Rejected }
    public enum PartRequestType { FromWarehouse, PurchaseNew }

    public class TechnicianStock { [Key] public int Id { get; set; } public int TechnicianId { get; set; } public int PartId { get; set; } public int Quantity { get; set; } [ForeignKey("TechnicianId")] public Technician? Technician { get; set; } [ForeignKey("PartId")] public SparePart? SparePart { get; set; } }

    public class OrderSparePart { [Key] public int Id { get; set; } public int OrderId { get; set; } public int PartId { get; set; } public int QuantityUsed { get; set; } public decimal SellingPriceAtTime { get; set; } [ForeignKey("OrderId")] public Order? Order { get; set; } [ForeignKey("PartId")] public SparePart? SparePart { get; set; } }

    public class PurchaseOrder { [Key] public int PurchaseId { get; set; } [Required] public string ItemName { get; set; } public int Quantity { get; set; } public decimal PurchasePrice { get; set; } public DateTime PurchaseDate { get; set; } public bool IsReceivedByStore { get; set; } = false; public bool IsPricedByManager { get; set; } = false; public string? SupplierName { get; set; } public string? SupplierPhone { get; set; } public string? SupplierLocation { get; set; } public string? Barcode { get; set; } public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash; }

    public enum DeductionSource { Company, Technician }
    public class Expense { [Key] public int Id { get; set; } [Required] public string Description { get; set; } public decimal Amount { get; set; } public DateTime Date { get; set; } = DateTime.Now; public string? RecordedBy { get; set; } public DeductionSource DeductionFrom { get; set; } = DeductionSource.Company; public int? TechnicianId { get; set; } [ForeignKey("TechnicianId")] public Technician? Technician { get; set; } public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash; }

    public class DamagedPart { [Key] public int Id { get; set; } public int PartId { get; set; } [ForeignKey("PartId")] public SparePart? SparePart { get; set; } public int Quantity { get; set; } [Required] public string Reason { get; set; } public DateTime Date { get; set; } = DateTime.Now; public decimal TotalLoss { get; set; } }

    public class Partner { [Key] public int Id { get; set; } [Required] public string Name { get; set; } public decimal SharePercentage { get; set; } }

    // ==========================================
    // 📌 موديلز الـ HR الجديدة 
    // ==========================================
    public enum LeaveType { Annual, Sick, Emergency, Unpaid }
    public enum LeaveStatus { Pending, Approved, Rejected }

    public class LeaveRequest
    {
        [Key] public int Id { get; set; }
        public int UserId { get; set; }
        [ForeignKey("UserId")] public SystemUser? User { get; set; }
        public LeaveType Type { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Reason { get; set; }
        public LeaveStatus Status { get; set; } = LeaveStatus.Pending;
        public DateTime RequestDate { get; set; } = DateTime.Now;
        public string? AdminNote { get; set; }
        public bool IsReturned { get; set; } = false;
        public DateTime? ActualReturnDate { get; set; }
    }

    public class PayrollSchedule
    {
        [Key] public int Id { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public DateTime ScheduledDate { get; set; }
        public bool IsProcessed { get; set; } = false;
        public string Note { get; set; }
    }

    public enum AttendanceStatus { Present, Absent }
    public class AttendanceRecord
    {
        [Key] public int Id { get; set; }
        public int UserId { get; set; }
        [ForeignKey("UserId")] public SystemUser? User { get; set; }
        public DateTime Date { get; set; }
        public AttendanceStatus Status { get; set; }
        public string? Note { get; set; }
    }

    public class PayrollRecord
    {
        [Key] public int Id { get; set; }
        public int UserId { get; set; }
        [ForeignKey("UserId")] public SystemUser? User { get; set; }
        public int Month { get; set; }
        public int Year { get; set; }
        public decimal BaseSalary { get; set; }
        public int PresentDays { get; set; }
        public int AbsentDays { get; set; }
        public decimal Deductions { get; set; }
        public decimal NetSalary { get; set; }
        public bool IsPaid { get; set; } = false;
        public DateTime? PaymentDate { get; set; }
    }
}