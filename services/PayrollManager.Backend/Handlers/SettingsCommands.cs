using PayrollManager.Backend.Contracts;
using PayrollManager.Backend.Rpc;
using PayrollManager.Backend.Validation;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;
using PayrollManager.Domain.Services;

namespace PayrollManager.Backend.Handlers;

/// <summary>
/// Company settings and database backup commands.
/// </summary>
public sealed class SettingsCommands
{
    private readonly Func<AppDbContext> _contextFactory;

    public SettingsCommands(Func<AppDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public void RegisterOn(CommandDispatcher dispatcher)
    {
        dispatcher.Register("get_company_settings", (_, ct) => GetAsync(ct));
        dispatcher.Register("update_company_settings", (p, ct) => UpdateAsync(p, ct));
        dispatcher.Register("create_backup", (_, ct) => CreateBackupAsync(ct));
        dispatcher.Register("list_backups", (_, ct) => ListBackups(ct));
    }

    private async Task<object?> GetAsync(CancellationToken ct)
    {
        await using var db = _contextFactory();
        var service = new CompanySettingsService(db);

        // GetSettingsAsync creates the single default record if none exists.
        var settings = await service.GetSettingsAsync();
        return CompanySettingsDto.From(settings);
    }

    private async Task<object?> UpdateAsync(System.Text.Json.JsonElement? parameters, CancellationToken ct)
    {
        var request = CommandDispatcher.RequireParams<UpdateCompanySettingsRequest>(parameters);

        var errors = SettingsValidator.Validate(request.Settings);
        if (errors.Count > 0)
        {
            throw RpcException.Validation(errors);
        }

        await using var db = _contextFactory();
        var service = new CompanySettingsService(db);

        var current = await service.GetSettingsAsync();
        var before = Describe(current);

        var input = request.Settings;
        current.CompanyName = input.CompanyName.Trim();
        current.CompanyAddress = input.CompanyAddress.Trim();
        current.TaxId = input.TaxId.Trim();
        current.PayPeriodsPerYear = input.PayPeriodsPerYear;
        current.DefaultHoursPerPeriod = input.DefaultHoursPerPeriod;
        current.SocialSecurityPercent = input.SocialSecurityPercent;
        current.MedicarePercent = input.MedicarePercent;
        current.SuiRatePercent = input.SuiRatePercent;
        current.SuiWageBase = input.SuiWageBase;
        current.ReceivesFullFutaCredit = input.ReceivesFullFutaCredit;

        await service.SaveSettingsAsync(current);

        // Audit on a separate context: SaveSettingsAsync manages its own change tracking, and
        // the append-only audit row should not ride inside the settings-dedup transaction.
        await using (var auditDb = _contextFactory())
        {
            auditDb.AuditLog.Add(new AuditLogEntry
            {
                Action = AuditAction.SettingsChanged,
                EntityType = nameof(CompanySettings),
                EntityId = current.Id,
                OldValue = before,
                NewValue = Describe(current),
                PerformedBy = Environment.UserName,
                ApplicationVersion = BackendInfo.Version
            });
            await auditDb.SaveChangesAsync(ct);
        }

        return CompanySettingsDto.From(current);
    }

    private Task<object?> CreateBackupAsync(CancellationToken ct)
    {
        var path = DbPaths.CreateBackup("manual")
            ?? throw RpcException.BusinessRule("There is no database to back up yet.");

        var info = new FileInfo(path);

        return Task.FromResult<object?>(new CreateBackupResponse
        {
            Backup = new BackupDto
            {
                FileName = info.Name,
                FullPath = info.FullName,
                SizeBytes = info.Length,
                CreatedUtc = info.CreationTimeUtc
            }
        });
    }

    private Task<object?> ListBackups(CancellationToken ct)
    {
        var dir = DbPaths.GetBackupDirectory();

        var backups = new DirectoryInfo(dir)
            .EnumerateFiles("payroll-*.db")
            .OrderByDescending(f => f.CreationTimeUtc)
            .Select(f => new BackupDto
            {
                FileName = f.Name,
                FullPath = f.FullName,
                SizeBytes = f.Length,
                CreatedUtc = f.CreationTimeUtc
            })
            .ToList();

        return Task.FromResult<object?>(new ListBackupsResponse { Backups = backups });
    }

    private static string Describe(CompanySettings s) =>
        $"name='{s.CompanyName}', periods={s.PayPeriodsPerYear}, ss={s.SocialSecurityPercent}%, " +
        $"medicare={s.MedicarePercent}%, sui={s.SuiRatePercent}%@{s.SuiWageBase:F0}, " +
        $"futaCredit={s.ReceivesFullFutaCredit}";
}
