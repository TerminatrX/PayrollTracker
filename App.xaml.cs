using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using PayrollManager.UI.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PayrollManager.UI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static IHost Host { get; } = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Database
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite($"Data Source={DbPaths.GetDatabasePath()}"));

                // Domain Services
                services.AddScoped<CompanySettingsService>();
                services.AddScoped<PayrollService>();
                services.AddScoped<AggregationService>();
                services.AddScoped<ExportService>();

                // ViewModels - Transient so each page gets a fresh instance
                // ViewModels now use AppDbContext for EF Core data access
                services.AddTransient<EmployeesViewModel>();
                services.AddTransient<EmployeeViewModel>();
                services.AddTransient<PayRunWizardViewModel>();
                services.AddTransient<PayStubViewModel>();
                services.AddTransient<PayStubDetailsViewModel>();
                services.AddTransient<ReportsViewModel>();
                services.AddTransient<SettingsViewModel>();
            })
            .Build();

        public static IServiceProvider Services => Host.Services;

        /// <summary>
        /// Gets a service of the specified type from the DI container.
        /// </summary>
        public static T GetService<T>() where T : class
        {
            return Host.Services.GetRequiredService<T>();
        }

        /// <summary>
        /// Initializes the singleton application object.
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            using (var scope = Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.Migrate();
                SeedSampleDataAsync(scope.ServiceProvider).GetAwaiter().GetResult();
            }

            m_window = new MainWindow();
            m_window.Activate();
            Utils.Helper.MainWindow = m_window;
        }

        public Window? m_window;

        private static async Task SeedSampleDataAsync(IServiceProvider services)
        {
            var db = services.GetRequiredService<AppDbContext>();
            var settingsService = services.GetRequiredService<CompanySettingsService>();

            if (db.Employees.Any())
            {
                return;
            }

            // Use CompanySettingsService to ensure single record exists
            var settings = await settingsService.GetSettingsAsync();
            
            // Update seed values if needed (only if using defaults)
            if (settings.CompanyName == "My Company" && settings.FederalTaxPercent == 12m)
            {
                settings.FederalTaxPercent = 10m;
                settings.StateTaxPercent = 5m;
                settings.SocialSecurityPercent = 6.2m;
                settings.MedicarePercent = 1.45m;
                settings.PayPeriodsPerYear = 26;
                await settingsService.SaveSettingsAsync(settings);
            }

            var employees = new[]
            {
                new Employee
                {
                    FirstName = "Avery",
                    LastName = "Nguyen",
                    IsActive = true,
                    IsHourly = false,
                    AnnualSalary = 85000m,
                    PreTax401kPercent = 4m,
                    HealthInsurancePerPeriod = 120m,
                    OtherDeductionsPerPeriod = 35m
                },
                new Employee
                {
                    FirstName = "Jordan",
                    LastName = "Lee",
                    IsActive = true,
                    IsHourly = true,
                    HourlyRate = 32m,
                    PreTax401kPercent = 3m,
                    HealthInsurancePerPeriod = 85m,
                    OtherDeductionsPerPeriod = 20m
                },
                new Employee
                {
                    FirstName = "Sam",
                    LastName = "Patel",
                    IsActive = true,
                    IsHourly = true,
                    HourlyRate = 28.5m,
                    PreTax401kPercent = 0m,
                    HealthInsurancePerPeriod = 0m,
                    OtherDeductionsPerPeriod = 0m
                }
            };

            db.Employees.AddRange(employees);
            await db.SaveChangesAsync();

            var payrollService = services.GetRequiredService<PayrollService>();
            var today = DateTime.Today;
            var periodEnd = today.AddDays(-1);
            var periodStart = periodEnd.AddDays(-13);

            var payRun = new PayRun
            {
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                PayDate = today
            };

            db.PayRuns.Add(payRun);
            await db.SaveChangesAsync();

            foreach (var employee in employees)
            {
                var hoursOverride = employee.IsHourly ? 80m : (decimal?)null;
                var payStub = await payrollService.GeneratePayStubAsync(employee, payRun, hoursOverride);
                db.PayStubs.Add(payStub);
            }

            await db.SaveChangesAsync();
        }
    }
}
