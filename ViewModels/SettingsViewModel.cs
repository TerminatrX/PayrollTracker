using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.UI.ViewModels;

/// <summary>
/// ViewModel for the Settings page including company profile, tax configuration, and pay periods.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private readonly CompanySettingsService _companySettingsService;
    private int _settingsId;

    public SettingsViewModel(AppDbContext dbContext, CompanySettingsService companySettingsService)
    {
        _dbContext = dbContext;
        _companySettingsService = companySettingsService;
        _ = LoadSettingsAsync();
    }
    // ═══════════════════════════════════════════════════════════════
    // COMPANY PROFILE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private string _companyAddress = string.Empty;

    [ObservableProperty]
    private string _taxId = string.Empty;

    [ObservableProperty]
    private string _logoPath = string.Empty;

    // ═══════════════════════════════════════════════════════════════
    // TAX CONFIGURATION - FEDERAL
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _autoFederalFiling = true;

    [ObservableProperty]
    private double _futaRate = 0.6;

    [ObservableProperty]
    private double _socialSecurityRate = 6.2;

    [ObservableProperty]
    private double _federalTaxPercent = 12;

    [ObservableProperty]
    private double _stateTaxPercent = 5;

    [ObservableProperty]
    private double _medicarePercent = 1.45;

    // ═══════════════════════════════════════════════════════════════
    // TAX CONFIGURATION - STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private string _stateCode = "CALIFORNIA";

    [ObservableProperty]
    private bool _suiEnabled = true;

    [ObservableProperty]
    private double _suiRate = 3.4;

    [ObservableProperty]
    private double _ettRate = 0.1;

    public bool ShowSuiWarning => SuiRate > 3.0;

    // ═══════════════════════════════════════════════════════════════
    // PAY PERIOD CONFIGURATION
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private int _payFrequencyIndex = 0; // 0=Bi-weekly, 1=Monthly, 2=Semi-monthly

    [ObservableProperty]
    private int _payPeriodsPerYear = 26;

    [ObservableProperty]
    private int _defaultHoursPerPeriod = 80;

    [ObservableProperty]
    private DateTimeOffset? _nextPeriodStart;

    [ObservableProperty]
    private DateTimeOffset? _estimatedPayDate;

    [ObservableProperty]
    private bool _offCycleEnabled = true;

    [ObservableProperty]
    private bool _autoApprovalEnabled = false;

    public string ProcessingWindowMessage => 
        $"Payroll must be submitted by Thursday at 5:00 PM PST to ensure on-time delivery for the pay date.";

    // ═══════════════════════════════════════════════════════════════
    // STATE
    // ═══════════════════════════════════════════════════════════════

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _hasChanges;

    // ═══════════════════════════════════════════════════════════════
    // COMMANDS
    // ═══════════════════════════════════════════════════════════════

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading settings...";

        try
        {
            // Use CompanySettingsService to get settings (ensures single record and uses cache)
            var settings = await _companySettingsService.GetSettingsAsync();

            _settingsId = settings.Id;
            CompanyName = settings.CompanyName;
            CompanyAddress = settings.CompanyAddress;
            TaxId = settings.TaxId;
            FederalTaxPercent = (double)settings.FederalTaxPercent;
            StateTaxPercent = (double)settings.StateTaxPercent;
            SocialSecurityRate = (double)settings.SocialSecurityPercent;
            MedicarePercent = (double)settings.MedicarePercent;
            PayPeriodsPerYear = settings.PayPeriodsPerYear;
            DefaultHoursPerPeriod = settings.DefaultHoursPerPeriod;

            // Set default dates
            var today = DateTime.Today;
            NextPeriodStart = new DateTimeOffset(today.AddDays(14 - (int)today.DayOfWeek));
            EstimatedPayDate = NextPeriodStart?.AddDays(5);

            // Determine frequency from periods per year
            PayFrequencyIndex = PayPeriodsPerYear switch
            {
                26 => 0, // Bi-weekly
                12 => 1, // Monthly
                24 => 2, // Semi-monthly
                _ => 0
            };

            HasChanges = false;
            StatusMessage = "Settings loaded";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        IsLoading = true;
        StatusMessage = "Saving settings...";

        try
        {
            // Create updated settings object
            var settings = new CompanySettings
            {
                Id = _settingsId,
                CompanyName = CompanyName,
                CompanyAddress = CompanyAddress,
                TaxId = TaxId,
                FederalTaxPercent = (decimal)FederalTaxPercent,
                StateTaxPercent = (decimal)StateTaxPercent,
                SocialSecurityPercent = (decimal)SocialSecurityRate,
                MedicarePercent = (decimal)MedicarePercent,
                PayPeriodsPerYear = PayPeriodsPerYear,
                DefaultHoursPerPeriod = DefaultHoursPerPeriod
            };

            // Save via service (ensures single record and invalidates cache)
            await _companySettingsService.SaveSettingsAsync(settings);
            
            HasChanges = false;
            StatusMessage = "Settings saved successfully";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error saving settings: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DiscardChanges()
    {
        await LoadSettingsAsync();
        HasChanges = false;
    }

    [RelayCommand]
    private void UploadLogo()
    {
        // Placeholder - will open file picker for logo upload
    }

    [RelayCommand]
    private void SignOut()
    {
        // Placeholder - will handle sign out
    }

    // Property change tracking for HasChanges
    partial void OnCompanyNameChanged(string value) => MarkAsChanged();
    partial void OnCompanyAddressChanged(string value) => MarkAsChanged();
    partial void OnTaxIdChanged(string value) => MarkAsChanged();
    partial void OnFederalTaxPercentChanged(double value) => MarkAsChanged();
    partial void OnStateTaxPercentChanged(double value) => MarkAsChanged();
    partial void OnSocialSecurityRateChanged(double value) => MarkAsChanged();
    partial void OnMedicarePercentChanged(double value) => MarkAsChanged();
    partial void OnPayPeriodsPerYearChanged(int value) => MarkAsChanged();
    partial void OnAutoFederalFilingChanged(bool value) => MarkAsChanged();
    partial void OnSuiEnabledChanged(bool value) => MarkAsChanged();
    partial void OnSuiRateChanged(double value)
    {
        MarkAsChanged();
        OnPropertyChanged(nameof(ShowSuiWarning));
    }
    partial void OnOffCycleEnabledChanged(bool value) => MarkAsChanged();
    partial void OnAutoApprovalEnabledChanged(bool value) => MarkAsChanged();
    partial void OnPayFrequencyIndexChanged(int value)
    {
        MarkAsChanged();
        PayPeriodsPerYear = value switch
        {
            0 => 26, // Bi-weekly
            1 => 12, // Monthly
            2 => 24, // Semi-monthly
            _ => 26
        };
    }

    private void MarkAsChanged()
    {
        HasChanges = true;
    }
}
