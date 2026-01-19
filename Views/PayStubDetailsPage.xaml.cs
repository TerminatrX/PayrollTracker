using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PayrollManager.Domain.Models;
using System.Text;

namespace PayrollManager.UI.Views;

public sealed partial class PayStubDetailsPage : Page
{
    private PayStub? _payStub;
    private Employee? _employee;

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

        // Earnings
        HoursWorkedText.Text = _payStub.HoursWorked.ToString("F2");
        PayRateText.Text = _employee.IsHourly 
            ? $"{_employee.HourlyRate:C2}/hr" 
            : $"{_employee.AnnualSalary:C2}/yr";
        GrossPayText.Text = _payStub.GrossPay.ToString("C2");

        // Pre-tax
        PreTax401kText.Text = $"-{_payStub.PreTax401kDeduction:C2}";

        // Taxes
        FederalTaxText.Text = $"-{_payStub.TaxFederal:C2}";
        StateTaxText.Text = $"-{_payStub.TaxState:C2}";
        SocialSecurityText.Text = $"-{_payStub.TaxSocialSecurity:C2}";
        MedicareText.Text = $"-{_payStub.TaxMedicare:C2}";
        TotalTaxesText.Text = $"-{_payStub.TotalTaxes:C2}";

        // Post-tax (estimate from model)
        var healthIns = _employee.HealthInsurancePerPeriod;
        var otherDed = _employee.OtherDeductionsPerPeriod;
        HealthInsuranceText.Text = $"-{healthIns:C2}";
        OtherDeductionsText.Text = $"-{otherDed:C2}";
        TotalDeductionsText.Text = $"-{_payStub.PostTaxDeductions:C2}";

        // YTD
        YtdGrossText.Text = _payStub.YtdGross.ToString("C2");
        
        // Calculate YTD taxes from YTD gross - YTD net - estimated deductions
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
            sb.AppendLine("Category,Amount");
            sb.AppendLine($"Gross Pay,{_payStub.GrossPay}");
            sb.AppendLine($"401k Deduction,-{_payStub.PreTax401kDeduction}");
            sb.AppendLine($"Federal Tax,-{_payStub.TaxFederal}");
            sb.AppendLine($"State Tax,-{_payStub.TaxState}");
            sb.AppendLine($"Social Security,-{_payStub.TaxSocialSecurity}");
            sb.AppendLine($"Medicare,-{_payStub.TaxMedicare}");
            sb.AppendLine($"Post-Tax Deductions,-{_payStub.PostTaxDeductions}");
            sb.AppendLine($"Net Pay,{_payStub.NetPay}");
            sb.AppendLine();
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

public class PayStubNavigationParameter
{
    public required PayStub PayStub { get; set; }
    public required Employee Employee { get; set; }
}
