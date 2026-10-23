using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;

namespace StonesHotspot.Core.Data;

public class HotspotDbContext(DbContextOptions<HotspotDbContext> options) : DbContext(options)
{
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<Voucher> Vouchers => Set<Voucher>();
    public DbSet<UserAccount> UserAccounts => Set<UserAccount>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<Staff> Staff => Set<Staff>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ProductGroup> ProductGroups => Set<ProductGroup>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<WebFilterRule> WebFilterRules => Set<WebFilterRule>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // FCFA amounts are whole numbers: stored as INTEGER.
        builder.Properties<Xaf>().HaveConversion<XafConverter>();

        // All dates are UTC; SQLite drops DateTime.Kind, so restore it on read.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();

        // Enums as text keep the database readable and safe when enum members are reordered.
        builder.Properties<TariffType>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<CountdownMode>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<VoucherStatus>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<LoginMethod>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<SessionEndReason>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<StaffRole>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<TransactionType>().HaveConversion<string>().HaveMaxLength(30);
        builder.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(20);
        builder.Properties<WebFilterKind>().HaveConversion<string>().HaveMaxLength(20);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tariff>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
        });

        b.Entity<Voucher>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(16).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.BatchName).HasMaxLength(100);
            e.HasIndex(x => x.BatchName);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.Tariff).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SoldByStaff).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.OwnsOne(x => x.Allowance);
            e.Navigation(x => x.Allowance).IsRequired();
        });

        b.Entity<UserAccount>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(64).IsRequired().UseCollation("NOCASE");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.PasswordHash).HasMaxLength(200);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.HasOne(x => x.Tariff).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.OwnsOne(x => x.Allowance);
            e.Navigation(x => x.Allowance).IsRequired();
        });

        b.Entity<Device>(e =>
        {
            e.Property(x => x.Mac).HasMaxLength(17).IsRequired();
            e.HasIndex(x => x.Mac).IsUnique();
            e.Property(x => x.LastIp).HasMaxLength(45);
            e.Property(x => x.Hostname).HasMaxLength(255);
            e.Property(x => x.Note).HasMaxLength(500);
        });

        b.Entity<Session>(e =>
        {
            e.Property(x => x.Ip).HasMaxLength(45);
            e.HasIndex(x => x.EndedAt);
            e.HasIndex(x => x.StartedAt);
            e.HasOne(x => x.Device).WithMany().OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Voucher).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.UserAccount).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Tariff).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Staff>(e =>
        {
            e.Property(x => x.Username).HasMaxLength(64).IsRequired().UseCollation("NOCASE");
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.DisplayName).HasMaxLength(100);
            e.Property(x => x.PasswordHash).HasMaxLength(200);
        });

        b.Entity<Shift>(e =>
        {
            e.HasOne(x => x.Staff).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Note).HasMaxLength(500);
        });

        b.Entity<ProductGroup>(e => e.Property(x => x.Name).HasMaxLength(100).IsRequired());

        b.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.VatRate).HasPrecision(5, 4);
            e.HasOne(x => x.Group).WithMany(g => g.Products).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Transaction>(e =>
        {
            e.Property(x => x.ReceiptNumber).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.ReceiptNumber);
            e.HasIndex(x => x.Timestamp);
            e.Property(x => x.Description).HasMaxLength(200);
            e.Property(x => x.ExternalReference).HasMaxLength(100);
            e.Property(x => x.VatRate).HasPrecision(5, 4);
            e.HasOne(x => x.Staff).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Shift).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Voucher).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.UserAccount).WithMany().OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Product).WithMany().OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<WebFilterRule>(e =>
        {
            e.Property(x => x.Pattern).HasMaxLength(255).IsRequired();
            e.Property(x => x.Category).HasMaxLength(50);
            e.HasIndex(x => new { x.Kind, x.Pattern }).IsUnique();
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(100);
        });

        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Actor).HasMaxLength(64);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.Details).HasMaxLength(2000);
            e.HasIndex(x => x.Timestamp);
        });
    }

    private sealed class XafConverter() : ValueConverter<Xaf, long>(x => x.Amount, v => new Xaf(v));

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
