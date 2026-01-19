using CommunityToolkit.Mvvm.ComponentModel;
using PayrollManager.Domain.Models;
using System.Collections;
using System.ComponentModel;

namespace PayrollManager.UI.ViewModels;

public partial class EmployeeViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly Dictionary<string, List<string>> _errors = new();

    private int? _id;
    private string _firstName = string.Empty;
    private string _lastName = string.Empty;
    private bool _isActive = true;
    private bool _isHourly;
    private decimal _annualSalary;
    private decimal _hourlyRate;
    private decimal _preTax401kPercent;
    private decimal _healthInsurancePerPeriod;
    private decimal _otherDeductionsPerPeriod;
    private DateTime? _hireDate;

    // Employment details
    private string _department = "Engineering";
    private string _jobTitle = string.Empty;
    private string _location = string.Empty;
    private string _manager = string.Empty;
    private string _workEmail = string.Empty;
    private int _employmentTypeIndex = 0;
    private int _flsaStatusIndex = 0;

    // Benefits
    private string _payFrequency = "Bi-Weekly";
    private string _healthTier = "Gold PPO";
    private string _retirementMatch = "4% Match";

    // Pay summary fields
    private DateTime? _lastPayDate;
    private decimal _lastGross;
    private decimal _lastNet;
    private decimal _ytdGross;
    private decimal _ytdTaxes;
    private decimal _ytdNet;
    private decimal _ytdBenefits;

    public int? Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string FirstName
    {
        get => _firstName;
        set
        {
            if (SetProperty(ref _firstName, value))
            {
                ValidateFirstName();
                OnPropertyChanged(nameof(FullName));
            }
        }
    }

    public string LastName
    {
        get => _lastName;
        set
        {
            if (SetProperty(ref _lastName, value))
            {
                ValidateLastName();
                OnPropertyChanged(nameof(FullName));
            }
        }
    }

    public string FullName => $"{FirstName} {LastName}".Trim();

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }

    public bool IsHourly
    {
        get => _isHourly;
        set
        {
            if (SetProperty(ref _isHourly, value))
            {
                OnPropertyChanged(nameof(PayTypeDisplay));
                ValidateCompensation();
            }
        }
    }

    public string PayTypeDisplay => IsHourly ? "Hourly" : "Salary";

    public decimal AnnualSalary
    {
        get => _annualSalary;
        set
        {
            if (SetProperty(ref _annualSalary, value))
            {
                ValidateCompensation();
                OnPropertyChanged(nameof(AnnualizedSalary));
            }
        }
    }

    public decimal HourlyRate
    {
        get => _hourlyRate;
        set
        {
            if (SetProperty(ref _hourlyRate, value))
            {
                ValidateCompensation();
                OnPropertyChanged(nameof(AnnualizedSalary));
            }
        }
    }

    // Computed annualized salary (for hourly: rate * 80 hours * 26 periods)
    public decimal AnnualizedSalary => IsHourly ? HourlyRate * 80m * 26m : AnnualSalary;

    public decimal PreTax401kPercent
    {
        get => _preTax401kPercent;
        set
        {
            if (SetProperty(ref _preTax401kPercent, value))
            {
                Validate401k();
            }
        }
    }

    public decimal HealthInsurancePerPeriod
    {
        get => _healthInsurancePerPeriod;
        set
        {
            if (SetProperty(ref _healthInsurancePerPeriod, value))
            {
                ValidateDeduction(nameof(HealthInsurancePerPeriod), value);
            }
        }
    }

    public decimal OtherDeductionsPerPeriod
    {
        get => _otherDeductionsPerPeriod;
        set
        {
            if (SetProperty(ref _otherDeductionsPerPeriod, value))
            {
                ValidateDeduction(nameof(OtherDeductionsPerPeriod), value);
            }
        }
    }

    public DateTime? HireDate
    {
        get => _hireDate;
        set
        {
            if (SetProperty(ref _hireDate, value))
            {
                OnPropertyChanged(nameof(HireDateDisplay));
            }
        }
    }

    public string HireDateDisplay => HireDate?.ToString("MMM dd, yyyy") ?? "Not set";

    // ═══════════════════════════════════════════════════════════════
    // EMPLOYMENT DETAILS
    // ═══════════════════════════════════════════════════════════════

    public string EmployeeId => Id.HasValue ? $"EMP{Id.Value:D3}" : "NEW";

    public string Department
    {
        get => _department;
        set => SetProperty(ref _department, value);
    }

    public string DepartmentCode => Department?.Length > 3 ? Department[..3] : Department ?? "";

    public string JobTitle
    {
        get => _jobTitle;
        set => SetProperty(ref _jobTitle, value);
    }

    public string Location
    {
        get => _location;
        set => SetProperty(ref _location, value);
    }

    public string Manager
    {
        get => _manager;
        set => SetProperty(ref _manager, value);
    }

    public string WorkEmail
    {
        get => _workEmail;
        set => SetProperty(ref _workEmail, value);
    }

    public int EmploymentTypeIndex
    {
        get => _employmentTypeIndex;
        set => SetProperty(ref _employmentTypeIndex, value);
    }

    public int FlsaStatusIndex
    {
        get => _flsaStatusIndex;
        set => SetProperty(ref _flsaStatusIndex, value);
    }

    // ═══════════════════════════════════════════════════════════════
    // COMPENSATION & BENEFITS
    // ═══════════════════════════════════════════════════════════════

    public string PayFrequency
    {
        get => _payFrequency;
        set => SetProperty(ref _payFrequency, value);
    }

    public string HealthTier
    {
        get => _healthTier;
        set => SetProperty(ref _healthTier, value);
    }

    public string RetirementMatch
    {
        get => _retirementMatch;
        set => SetProperty(ref _retirementMatch, value);
    }

    // ═══════════════════════════════════════════════════════════════
    // PAY SUMMARY
    // ═══════════════════════════════════════════════════════════════
    public DateTime? LastPayDate
    {
        get => _lastPayDate;
        set => SetProperty(ref _lastPayDate, value);
    }

    public decimal LastGross
    {
        get => _lastGross;
        set => SetProperty(ref _lastGross, value);
    }

    public decimal LastNet
    {
        get => _lastNet;
        set => SetProperty(ref _lastNet, value);
    }

    public decimal YtdGross
    {
        get => _ytdGross;
        set => SetProperty(ref _ytdGross, value);
    }

    public decimal YtdTaxes
    {
        get => _ytdTaxes;
        set => SetProperty(ref _ytdTaxes, value);
    }

    public decimal YtdNet
    {
        get => _ytdNet;
        set => SetProperty(ref _ytdNet, value);
    }

    public decimal YtdBenefits
    {
        get => _ytdBenefits;
        set => SetProperty(ref _ytdBenefits, value);
    }

    // YTD Display properties
    public string YtdAsOfDate => $"AS OF {DateTime.Today:MMM dd}".ToUpper();
    public string YtdTaxesDisplay => $"(${YtdTaxes:N2})";
    public string YtdBenefitsDisplay => $"(${YtdBenefits:N2})";

    // ═══════════════════════════════════════════════════════════════
    // VALIDATION (INotifyDataErrorInfo)
    // ═══════════════════════════════════════════════════════════════
    public bool HasErrors => _errors.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
            return _errors.SelectMany(e => e.Value);
        
        return _errors.TryGetValue(propertyName, out var errors) ? errors : Enumerable.Empty<string>();
    }

    public string? GetFirstError(string propertyName)
    {
        return _errors.TryGetValue(propertyName, out var errors) && errors.Count > 0 ? errors[0] : null;
    }

    private void SetError(string propertyName, string? error)
    {
        if (string.IsNullOrEmpty(error))
        {
            if (_errors.Remove(propertyName))
            {
                ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
                OnPropertyChanged(nameof(HasErrors));
            }
        }
        else
        {
            _errors[propertyName] = new List<string> { error };
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    private void ValidateFirstName()
    {
        SetError(nameof(FirstName), string.IsNullOrWhiteSpace(FirstName) ? "First name is required" : null);
    }

    private void ValidateLastName()
    {
        SetError(nameof(LastName), string.IsNullOrWhiteSpace(LastName) ? "Last name is required" : null);
    }

    private void ValidateCompensation()
    {
        if (IsHourly)
        {
            SetError(nameof(HourlyRate), HourlyRate <= 0 ? "Hourly rate must be greater than 0" : null);
            SetError(nameof(AnnualSalary), null);
        }
        else
        {
            SetError(nameof(AnnualSalary), AnnualSalary <= 0 ? "Annual salary must be greater than 0" : null);
            SetError(nameof(HourlyRate), null);
        }
    }

    private void Validate401k()
    {
        SetError(nameof(PreTax401kPercent), 
            PreTax401kPercent < 0 || PreTax401kPercent > 25 
                ? "401k % must be between 0 and 25" 
                : null);
    }

    private void ValidateDeduction(string propertyName, decimal value)
    {
        SetError(propertyName, value < 0 ? "Value must be 0 or greater" : null);
    }

    public void ValidateAll()
    {
        ValidateFirstName();
        ValidateLastName();
        ValidateCompensation();
        Validate401k();
        ValidateDeduction(nameof(HealthInsurancePerPeriod), HealthInsurancePerPeriod);
        ValidateDeduction(nameof(OtherDeductionsPerPeriod), OtherDeductionsPerPeriod);
    }

    public void LoadFrom(Employee employee)
    {
        Id = employee.Id;
        FirstName = employee.FirstName;
        LastName = employee.LastName;
        IsActive = employee.IsActive;
        IsHourly = employee.IsHourly;
        AnnualSalary = employee.AnnualSalary;
        HourlyRate = employee.HourlyRate;
        PreTax401kPercent = employee.PreTax401kPercent;
        HealthInsurancePerPeriod = employee.HealthInsurancePerPeriod;
        OtherDeductionsPerPeriod = employee.OtherDeductionsPerPeriod;
        JobTitle = employee.JobTitle ?? string.Empty;
        Department = employee.Department ?? "Engineering";
        _errors.Clear();
    }

    public void ApplyTo(Employee employee)
    {
        employee.FirstName = FirstName.Trim();
        employee.LastName = LastName.Trim();
        employee.IsActive = IsActive;
        employee.IsHourly = IsHourly;
        employee.AnnualSalary = AnnualSalary;
        employee.HourlyRate = HourlyRate;
        employee.PreTax401kPercent = PreTax401kPercent;
        employee.HealthInsurancePerPeriod = HealthInsurancePerPeriod;
        employee.OtherDeductionsPerPeriod = OtherDeductionsPerPeriod;
        employee.JobTitle = string.IsNullOrWhiteSpace(JobTitle) ? null : JobTitle.Trim();
        employee.Department = string.IsNullOrWhiteSpace(Department) ? null : Department.Trim();
    }

    public void Reset()
    {
        Id = null;
        FirstName = string.Empty;
        LastName = string.Empty;
        IsActive = true;
        IsHourly = false;
        AnnualSalary = 0;
        HourlyRate = 0;
        PreTax401kPercent = 0;
        HealthInsurancePerPeriod = 0;
        OtherDeductionsPerPeriod = 0;
        HireDate = null;
        
        // Employment details
        Department = "Engineering";
        JobTitle = string.Empty;
        Location = string.Empty;
        Manager = string.Empty;
        WorkEmail = string.Empty;
        EmploymentTypeIndex = 0;
        FlsaStatusIndex = 0;
        
        // Benefits
        PayFrequency = "Bi-Weekly";
        HealthTier = "Gold PPO";
        RetirementMatch = "4% Match";
        
        // Pay summary
        LastPayDate = null;
        LastGross = 0;
        LastNet = 0;
        YtdGross = 0;
        YtdTaxes = 0;
        YtdNet = 0;
        YtdBenefits = 0;
        
        _errors.Clear();
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(EmployeeId));
        OnPropertyChanged(nameof(HireDateDisplay));
    }
}
