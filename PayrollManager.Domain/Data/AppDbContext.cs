using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<PayRun> PayRuns => Set<PayRun>();
    public DbSet<PayStub> PayStubs => Set<PayStub>();
    public DbSet<EarningLine> EarningLines => Set<EarningLine>();
    public DbSet<DeductionLine> DeductionLines => Set<DeductionLine>();
    public DbSet<TaxLine> TaxLines => Set<TaxLine>();
    public DbSet<CompanySettings> CompanySettings => Set<CompanySettings>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dbPath = DbPaths.GetDatabasePath();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Employee>()
            .HasMany(e => e.PayStubs)
            .WithOne(s => s.Employee)
            .HasForeignKey(s => s.EmployeeId);

        modelBuilder.Entity<PayRun>()
            .HasMany(p => p.PayStubs)
            .WithOne(s => s.PayRun)
            .HasForeignKey(s => s.PayRunId);

        modelBuilder.Entity<PayStub>()
            .HasMany(ps => ps.EarningLines)
            .WithOne(el => el.PayStub)
            .HasForeignKey(el => el.PayStubId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PayStub>()
            .HasMany(ps => ps.DeductionLines)
            .WithOne(dl => dl.PayStub)
            .HasForeignKey(dl => dl.PayStubId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PayStub>()
            .HasMany(ps => ps.TaxLines)
            .WithOne(tl => tl.PayStub)
            .HasForeignKey(tl => tl.PayStubId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<EarningLine>()
            .Property(el => el.Type)
            .HasConversion<string>();

        modelBuilder.Entity<DeductionLine>()
            .Property(dl => dl.Type)
            .HasConversion<string>();

        modelBuilder.Entity<TaxLine>()
            .Property(tl => tl.Type)
            .HasConversion<string>();
    }
}
