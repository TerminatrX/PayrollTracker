using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Models;
using System.Collections.ObjectModel;
using System.Text;

namespace PayrollManager.UI.Views;

public sealed partial class PayStubDetailsPage : Page
{
    private PayStub? _payStub;
    private Employee? _employee;

    public ObservableCollection<EarningLineDisplay> EarningLines { get; } = new();

    public PayStubDetailsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        
        if (e.Parameter is PayStubNavigationParameter param)
        {
            _payStub = param.PayStub;
            _employee = param.Employee;
            LoadPayStubDetails();
        }
    }

    private void LoadPayStubDetails()
    {
        if (_payStub == null || _employee == null) return;

        // Header
        EmployeeNameText.Text = _employee.FullName;
        PayPeriodText.Text = $"Pay Period: {_payStub.PayRun?.PeriodStart:MMM dd} - {_payStub.PayRun?.PeriodEnd:MMM dd, yyyy} | Pay Date: {_payStub.PayRun?.PayDate:MMM dd, yyyy}";
        NetPayText.Text = _payStub.NetPay.ToString("C2");

        // Load earning lines
        EarningLines.Clear();
        foreach (var line in _payStub.EarningLines.OrderBy(e => e.Type))
        {
            EarningLines.Add(new EarningLineDisplay(line));
        }

        // If no earning lines exist (legacy data), create a synthetic one
        if (EarningLines.Count == 0)
        {
            EarningLines.Add(new EarningLineDisplay
            {
                TypeDisplay = _employee.IsHourly ? "Regular" : "Salary",
                Description = _employee.IsHourly ? "Regular Pay" : "Salary",
                Hours = _payStub.HoursWorked,
                Rate = _employee.IsHourly ? _employee.HourlyRate : _payStub.GrossPay,
                Amount = _payStub.GrossPay
            });
        }

        EarningsGrid.ItemsSource = EarningLines;

        // Gross Pay
        GrossPayText.Text = _payStub.GrossPay.ToString("C2");

        // Pre-tax
        PreTax401kText.Text = $"-{_payStub.PreTax401kDeduction:C2}";

        // Taxes
        FederalTaxText.Text = $"-{_payStub.TaxFederal:C2}";
        StateTaxText.Text = $"-{_payStub.TaxState:C2}";
        SocialSecurityText.Text = $"-{_payStub.TaxSocialSecurity:C2}";
        MedicareText.Text = $"-{_payStub.TaxMedicare:C2}";
        TotalTaxesText.Text = $"-{_payStub.TotalTaxes:C2}";

        // Post-tax
        var healthIns = _employee.HealthInsurancePerPeriod;
        var otherDed = _employee.OtherDeductionsPerPeriod;
        HealthInsuranceText.Text = $"-{healthIns:C2}";
        OtherDeductionsText.Text = $"-{otherDed:C2}";
        TotalDeductionsText.Text = $"-{_payStub.PostTaxDeductions:C2}";

        // Earnings breakdown
        RegularEarningsText.Text = _payStub.RegularEarnings.ToString("C2");
        OvertimeEarningsText.Text = _payStub.OvertimeEarnings.ToString("C2");
        BonusEarningsText.Text = _payStub.BonusEarnings.ToString("C2");
        CommissionEarningsText.Text = _payStub.CommissionEarnings.ToString("C2");

        // YTD
        YtdGrossText.Text = _payStub.YtdGross.ToString("C2");
        var ytdTaxesEstimate = _payStub.YtdGross - _payStub.YtdNet;
        YtdTaxesText.Text = ytdTaxesEstimate.ToString("C2");
        YtdNetText.Text = _payStub.YtdNet.ToString("C2");
    }

    private async void ExportCsv_Click(object sender, RoutedEventArgs e)
    {
        if (_payStub == null || _employee == null) return;

        try
        {
            var fileName = $"paystub_{_employee.LastName}_{_payStub.PayRun?.PayDate:yyyyMMdd}.csv";
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);

            var sb = new StringBuilder();
            sb.AppendLine("Pay Stub Export");
            sb.AppendLine($"Employee,{_employee.FullName}");
            sb.AppendLine($"Pay Period,{_payStub.PayRun?.PeriodStart:d} - {_payStub.PayRun?.PeriodEnd:d}");
            sb.AppendLine($"Pay Date,{_payStub.PayRun?.PayDate:d}");
            sb.AppendLine();

            // Earning lines
            sb.AppendLine("EARNINGS");
            sb.AppendLine("Type,Description,Hours,Rate,Amount");
            foreach (var line in _payStub.EarningLines)
            {
                sb.AppendLine($"{line.Type},{line.Description},{line.Hours},{line.Rate},{line.Amount}");
            }
            sb.AppendLine($",,,,Gross Pay,{_payStub.GrossPay}");
            sb.AppendLine();

            sb.AppendLine("DEDUCTIONS & TAXES");
            sb.AppendLine($"401k Deduction,-{_payStub.PreTax401kDeduction}");
            sb.AppendLine($"Federal Tax,-{_payStub.TaxFederal}");
            sb.AppendLine($"State Tax,-{_payStub.TaxState}");
            sb.AppendLine($"Social Security,-{_payStub.TaxSocialSecurity}");
            sb.AppendLine($"Medicare,-{_payStub.TaxMedicare}");
            sb.AppendLine($"Post-Tax Deductions,-{_payStub.PostTaxDeductions}");
            sb.AppendLine();

            sb.AppendLine($"Net Pay,{_payStub.NetPay}");
            sb.AppendLine();
            sb.AppendLine("YEAR-TO-DATE");
            sb.AppendLine($"YTD Gross,{_payStub.YtdGross}");
            sb.AppendLine($"YTD Net,{_payStub.YtdNet}");

            await File.WriteAllTextAsync(path, sb.ToString(), Encoding.UTF8);

            var dialog = new ContentDialog
            {
                Title = "Export Complete",
                Content = $"Pay stub exported to:\n{path}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Export Failed",
                Content = ex.Message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }

    private async void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Implement PDF export using a library like QuestPDF or iTextSharp
        var dialog = new ContentDialog
        {
            Title = "Coming Soon",
            Content = "PDF export functionality will be available in a future update.",
            CloseButtonText = "OK",
            XamlRoot = this.XamlRoot
        };
        await dialog.ShowAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }
}

public class EarningLineDisplay
{
    public EarningLineDisplay() { }

    public EarningLineDisplay(EarningLine line)
    {
        TypeDisplay = line.Type.ToString();
        Description = line.Description;
        Hours = line.Hours;
        Rate = line.Rate;
        Amount = line.Amount;
    }

    public string TypeDisplay { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Hours { get; set; }
    public decimal Rate { get; set; }
    public decimal Amount { get; set; }

    public string HoursDisplay => Hours > 0 ? Hours.ToString("F2") : "—";
    public string RateDisplay => Rate.ToString("C2");
    public string AmountDisplay => Amount.ToString("C2");
    public bool IsTotalRow { get; set; }
}

public class PayStubNavigationParameter
{
    public required PayStub PayStub { get; set; }
    public required Employee Employee { get; set; }
}
