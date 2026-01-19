using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;

namespace PayrollManager.UI.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppDbContext _dbContext;
    private int _settingsId;
    private string _companyName = string.Empty;
    private string _companyAddress = string.Empty;
    private string _taxId = string.Empty;
    private double _federalTaxPercent = 12;
    private double _stateTaxPercent = 5;
    private double _socialSecurityPercent = 6.2;
    private double _medicarePercent = 1.45;
    private int _payPeriodsPerYear = 26;
    private int _defaultHoursPerPeriod = 80;
    private bool _isLoading;
    private string _statusMessage = string.Empty;

    public SettingsViewModel(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string CompanyName
    {
        get => _companyName;
        set => SetProperty(ref _companyName, value);
    }

    public string CompanyAddress
    {
        get => _companyAddress;
        set => SetProperty(ref _companyAddress, value);
    }

    public string TaxId
    {
        get => _taxId;
        set => SetProperty(ref _taxId, value);
    }

    public double FederalTaxPercent
    {
        get => _federalTaxPercent;
        set => SetProperty(ref _federalTaxPercent, value);
    }

    public double StateTaxPercent
    {
        get => _stateTaxPercent;
        set => SetProperty(ref _stateTaxPercent, value);
    }

    public double SocialSecurityPercent
    {
        get => _socialSecurityPercent;
        set => SetProperty(ref _socialSecurityPercent, value);
    }

    public double MedicarePercent
    {
        get => _medicarePercent;
        set => SetProperty(ref _medicarePercent, value);
    }

    public int PayPeriodsPerYear
    {
        get => _payPeriodsPerYear;
        set => SetProperty(ref _payPeriodsPerYear, value);
    }

    public int DefaultHoursPerPeriod
    {
        get => _defaultHoursPerPeriod;
        set => SetProperty(ref _defaultHoursPerPeriod, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    [RelayCommand]
    public async Task LoadSettingsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading settings...";

        try
        {
            var settings = await _dbContext.CompanySettings.FirstOrDefaultAsync();
            
            if (settings == null)
            {
                settings = new CompanySettings
                {
                    CompanyName = "My Company",
                    PayPeriodsPerYear = 26,
                    FederalTaxPercent = 12m,
                    StateTaxPercent = 5m,
                    SocialSecurityPercent = 6.2m,
                    MedicarePercent = 1.45m,
                    DefaultHoursPerPeriod = 80
                };
                _dbContext.CompanySettings.Add(settings);
                await _dbContext.SaveChangesAsync();
            }

            _settingsId = settings.Id;
            CompanyName = settings.CompanyName;
            CompanyAddress = settings.CompanyAddress;
            TaxId = settings.TaxId;
            FederalTaxPercent = (double)settings.FederalTaxPercent;
            StateTaxPercent = (double)settings.StateTaxPercent;
            SocialSecurityPercent = (double)settings.SocialSecurityPercent;
            MedicarePercent = (double)settings.MedicarePercent;
            PayPeriodsPerYear = settings.PayPeriodsPerYear;
            DefaultHoursPerPeriod = settings.DefaultHoursPerPeriod;

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
    public async Task SaveSettingsAsync()
    {
        IsLoading = true;
        StatusMessage = "Saving settings...";

        try
        {
            var settings = await _dbContext.CompanySettings.FindAsync(_settingsId);
            
            if (settings != null)
            {
                settings.CompanyName = CompanyName;
                settings.CompanyAddress = CompanyAddress;
                settings.TaxId = TaxId;
                settings.FederalTaxPercent = (decimal)FederalTaxPercent;
                settings.StateTaxPercent = (decimal)StateTaxPercent;
                settings.SocialSecurityPercent = (decimal)SocialSecurityPercent;
                settings.MedicarePercent = (decimal)MedicarePercent;
                settings.PayPeriodsPerYear = PayPeriodsPerYear;
                settings.DefaultHoursPerPeriod = DefaultHoursPerPeriod;

                await _dbContext.SaveChangesAsync();
                StatusMessage = "Settings saved successfully";
            }
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
}
