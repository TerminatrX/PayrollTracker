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
    }
}
