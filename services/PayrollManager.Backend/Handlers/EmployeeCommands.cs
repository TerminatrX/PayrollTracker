using Microsoft.EntityFrameworkCore;
using PayrollManager.Backend.Contracts;
using PayrollManager.Backend.Rpc;
using PayrollManager.Backend.Validation;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services.Security;

namespace PayrollManager.Backend.Handlers;

/// <summary>
/// Employee commands. Each is a narrow business operation; none exposes SQL or table shape.
/// </summary>
public sealed class EmployeeCommands
{
    private readonly Func<AppDbContext> _contextFactory;
    private readonly ISsnProtector _ssnProtector;

    public EmployeeCommands(Func<AppDbContext> contextFactory, ISsnProtector? ssnProtector = null)
    {
        _contextFactory = contextFactory;
        _ssnProtector = ssnProtector ?? new DpapiSsnProtector();
    }

    public void RegisterOn(CommandDispatcher dispatcher)
    {
        dispatcher.Register("get_employees", (p, ct) => GetEmployeesAsync(p, ct));
        dispatcher.Register("get_employee", (p, ct) => GetEmployeeAsync(p, ct));
        dispatcher.Register("create_employee", (p, ct) => CreateEmployeeAsync(p, ct));
        dispatcher.Register("update_employee", (p, ct) => UpdateEmployeeAsync(p, ct));
    }

    private async Task<object?> GetEmployeesAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.ParamsOrDefault<GetEmployeesRequest>(parameters);

        await using var db = _contextFactory();

        var query = db.Employees.AsNoTracking().AsQueryable();

        if (!request.IncludeInactive)
        {
            query = query.Where(e => e.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim();

            // EF.Functions.Like keeps the filter in SQL; a client-side ToLower() would pull
            // the whole table into memory.
            var pattern = $"%{term}%";
            query = query.Where(e =>
                EF.Functions.Like(e.FirstName, pattern) ||
                EF.Functions.Like(e.LastName, pattern) ||
                EF.Functions.Like(e.JobTitle ?? string.Empty, pattern) ||
                EF.Functions.Like(e.Department ?? string.Empty, pattern));
        }

        var employees = await query
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        return employees.Select(EmployeeDto.From).ToList();
    }

    private async Task<object?> GetEmployeeAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<GetEmployeeRequest>(parameters);

        await using var db = _contextFactory();

        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct)
            ?? throw RpcException.NotFound($"No employee with id {request.EmployeeId}.");

        return EmployeeDto.From(employee);
    }

    private async Task<object?> CreateEmployeeAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<CreateEmployeeRequest>(parameters);

        var errors = EmployeeValidator.Validate(request.Employee);
        if (errors.Count > 0)
        {
            throw RpcException.Validation(errors);
        }

        await using var db = _contextFactory();

        var employee = new Employee();
        Apply(request.Employee, employee);

        db.Employees.Add(employee);

        db.AuditLog.Add(new AuditLogEntry
        {
            Action = AuditAction.EmployeeCreated,
            EntityType = nameof(Employee),
            NewValue = $"{employee.FullName} ({(employee.IsHourly ? $"{employee.HourlyRate:C}/hr" : $"{employee.AnnualSalary:C}/yr")})",
            PerformedBy = Environment.UserName,
            ApplicationVersion = BackendInfo.Version
        });

        await db.SaveChangesAsync(ct);

        return EmployeeDto.From(employee);
    }

    private async Task<object?> UpdateEmployeeAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<UpdateEmployeeRequest>(parameters);

        var errors = EmployeeValidator.Validate(request.Employee);
        if (errors.Count > 0)
        {
            throw RpcException.Validation(errors);
        }

        await using var db = _contextFactory();

        var employee = await db.Employees.FirstOrDefaultAsync(e => e.Id == request.EmployeeId, ct)
            ?? throw RpcException.NotFound($"No employee with id {request.EmployeeId}.");

        // Capture before/after for the audit trail: "who changed this rate" is the single most
        // common payroll question months after the fact.
        var before = DescribeCompensation(employee);
        Apply(request.Employee, employee);
        var after = DescribeCompensation(employee);

        if (before != after)
        {
            db.AuditLog.Add(new AuditLogEntry
            {
                Action = AuditAction.CompensationChanged,
                EntityType = nameof(Employee),
                EntityId = employee.Id,
                OldValue = before,
                NewValue = after,
                PerformedBy = Environment.UserName,
                ApplicationVersion = BackendInfo.Version
            });
        }
        else
        {
            db.AuditLog.Add(new AuditLogEntry
            {
                Action = AuditAction.EmployeeUpdated,
                EntityType = nameof(Employee),
                EntityId = employee.Id,
                NewValue = employee.FullName,
                PerformedBy = Environment.UserName,
                ApplicationVersion = BackendInfo.Version
            });
        }

        await db.SaveChangesAsync(ct);

        return EmployeeDto.From(employee);
    }

    private static string DescribeCompensation(Employee e) =>
        e.IsHourly
            ? $"hourly {e.HourlyRate:F2}, 401k {e.PreTax401kPercent:F2}%, health {e.HealthInsurancePerPeriod:F2}"
            : $"salary {e.AnnualSalary:F2}, 401k {e.PreTax401kPercent:F2}%, health {e.HealthInsurancePerPeriod:F2}";

    private void Apply(EmployeeInput input, Employee employee)
    {
        employee.FirstName = input.FirstName.Trim();
        employee.LastName = input.LastName.Trim();
        employee.IsActive = input.IsActive;
        employee.IsHourly = input.IsHourly;

        // Zero the irrelevant compensation field so a pay type switch cannot leave a stale
        // salary sitting behind an hourly employee.
        employee.AnnualSalary = input.IsHourly ? 0m : input.AnnualSalary;
        employee.HourlyRate = input.IsHourly ? input.HourlyRate : 0m;

        employee.DefaultHoursPerPeriod = input.DefaultHoursPerPeriod;
        employee.PreTax401kPercent = input.PreTax401kPercent;
        employee.HealthInsurancePerPeriod = input.HealthInsurancePerPeriod;
        employee.OtherDeductionsPerPeriod = input.OtherDeductionsPerPeriod;
        employee.JobTitle = string.IsNullOrWhiteSpace(input.JobTitle) ? null : input.JobTitle.Trim();
        employee.Department = string.IsNullOrWhiteSpace(input.Department) ? null : input.Department.Trim();
        employee.HireDate = input.HireDate;
        employee.TerminationDate = input.TerminationDate;

        employee.StreetAddress = Clean(input.StreetAddress);
        employee.City = Clean(input.City);
        employee.State = Clean(input.State);
        employee.PostalCode = Clean(input.PostalCode);

        // SSN is write-only: a supplied value replaces what's on file; a blank leaves it
        // unchanged (so editing an employee without re-typing the SSN keeps the stored one).
        // The plaintext SSN never leaves this method - only the ciphertext and last-4 persist.
        if (!string.IsNullOrWhiteSpace(input.Ssn))
        {
            var protectedSsn = _ssnProtector.Protect(input.Ssn);
            employee.SsnEncrypted = protectedSsn.Encrypted;
            employee.SsnLast4 = protectedSsn.Last4;
        }

        employee.W4OnFile = input.W4OnFile;
        employee.FilingStatus = input.FilingStatus;
        employee.W4MultipleJobsChecked = input.W4MultipleJobsChecked;
        employee.W4DependentsAndOtherCredits = input.W4DependentsAndOtherCredits;
        employee.W4OtherIncome = input.W4OtherIncome;
        employee.W4Deductions = input.W4Deductions;
        employee.W4ExtraWithholding = input.W4ExtraWithholding;

        employee.IlBasicAllowances = input.IlBasicAllowances;
        employee.IlAdditionalAllowances = input.IlAdditionalAllowances;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class BackendInfo
{
    public const string Version = "2.0-sidecar";
}
