using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

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
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite($"Data Source={DbPaths.GetDatabasePath()}"));

                services.AddScoped<CompanySettings>(sp =>
                {
                    var db = sp.GetRequiredService<AppDbContext>();
                    var settings = db.CompanySettings.FirstOrDefault();
                    if (settings == null)
                    {
                        settings = new CompanySettings();
                        db.CompanySettings.Add(settings);
                        db.SaveChanges();
                    }

                    return settings;
                });

                services.AddScoped<PayrollService>();
            })
            .Build();

        public static IServiceProvider Services => Host.Services;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
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

            if (db.Employees.Any())
            {
                return;
            }

            var settings = await db.CompanySettings.FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new CompanySettings
                {
                    FederalTaxPercent = 10m,
                    StateTaxPercent = 5m,
                    SocialSecurityPercent = 6.2m,
                    MedicarePercent = 1.45m,
                    PayPeriodsPerYear = 26
                };
                db.CompanySettings.Add(settings);
                await db.SaveChangesAsync();
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
