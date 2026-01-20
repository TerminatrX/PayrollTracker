using Microsoft.EntityFrameworkCore;
using PayrollManager.Domain.Data;
using PayrollManager.Domain.Models;

namespace PayrollManager.Domain.Services;

/// <summary>
/// Service for managing company settings with caching and single-record enforcement.
/// Ensures exactly one active CompanySettings record exists and caches it for performance.
/// </summary>
public class CompanySettingsService
{
    private readonly AppDbContext _dbContext;
    private CompanySettings? _cachedSettings;
    private readonly object _lockObject = new();

    public CompanySettingsService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <summary>
    /// Gets the current company settings, using cache if available.
    /// Ensures exactly one active record exists.
    /// </summary>
    public Task<CompanySettings> GetSettingsAsync()
    {
        // Double-check locking pattern for thread safety
        if (_cachedSettings != null)
        {
            return Task.FromResult(_cachedSettings);
        }

        lock (_lockObject)
        {
            if (_cachedSettings != null)
            {
                return Task.FromResult(_cachedSettings);
            }

            // Load synchronously within lock to prevent multiple DB queries
            var settings = _dbContext.CompanySettings.FirstOrDefault();
            
            if (settings == null)
            {
                // Create default settings if none exist
                settings = new CompanySettings
                {
                    CompanyName = "My Company",
                    CompanyAddress = string.Empty,
                    TaxId = string.Empty,
                    FederalTaxPercent = 12m,
                    StateTaxPercent = 5m,
                    SocialSecurityPercent = 6.2m,
                    MedicarePercent = 1.45m,
                    PayPeriodsPerYear = 26,
                    DefaultHoursPerPeriod = 80
                };
                _dbContext.CompanySettings.Add(settings);
                _dbContext.SaveChanges();
            }
            else
            {
                // Ensure only one record exists - delete any additional records
                var allSettings = _dbContext.CompanySettings.ToList();
                if (allSettings.Count > 1)
                {
                    // Keep the first one (oldest or most recent), delete others
                    var toKeep = allSettings.OrderByDescending(s => s.Id).First();
                    var toDelete = allSettings.Where(s => s.Id != toKeep.Id).ToList();
                    _dbContext.CompanySettings.RemoveRange(toDelete);
                    _dbContext.SaveChanges();
                    
                    // Update reference if we kept a different one
                    if (toKeep.Id != settings.Id)
                    {
                        settings = toKeep;
                    }
                }
            }

            _cachedSettings = settings;
            return Task.FromResult(_cachedSettings);
        }
    }

    /// <summary>
    /// Gets the current company settings synchronously (uses cache if available).
    /// Use this method when you're already in a synchronous context.
    /// </summary>
    public CompanySettings GetSettings()
    {
        if (_cachedSettings != null)
        {
            return _cachedSettings;
        }

        lock (_lockObject)
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings;
            }

            var settings = _dbContext.CompanySettings.FirstOrDefault();
            
            if (settings == null)
            {
                settings = new CompanySettings
                {
                    CompanyName = "My Company",
                    CompanyAddress = string.Empty,
                    TaxId = string.Empty,
                    FederalTaxPercent = 12m,
                    StateTaxPercent = 5m,
                    SocialSecurityPercent = 6.2m,
                    MedicarePercent = 1.45m,
                    PayPeriodsPerYear = 26,
                    DefaultHoursPerPeriod = 80
                };
                _dbContext.CompanySettings.Add(settings);
                _dbContext.SaveChanges();
            }
            else
            {
                // Ensure only one record exists
                var allSettings = _dbContext.CompanySettings.ToList();
                if (allSettings.Count > 1)
                {
                    var toKeep = allSettings.OrderByDescending(s => s.Id).First();
                    var toDelete = allSettings.Where(s => s.Id != toKeep.Id).ToList();
                    _dbContext.CompanySettings.RemoveRange(toDelete);
                    _dbContext.SaveChanges();
                    
                    if (toKeep.Id != settings.Id)
                    {
                        settings = toKeep;
                    }
                }
            }

            _cachedSettings = settings;
            return _cachedSettings;
        }
    }

    /// <summary>
    /// Saves the company settings and invalidates the cache.
    /// Ensures exactly one record exists after save.
    /// </summary>
    public async Task SaveSettingsAsync(CompanySettings settings)
    {
        // Ensure only one record exists before saving
        var existingSettings = await _dbContext.CompanySettings.ToListAsync();
        
        if (existingSettings.Count > 1)
        {
            // Delete all but the one we're updating
            var toKeep = existingSettings.OrderByDescending(s => s.Id).First();
            var toDelete = existingSettings.Where(s => s.Id != toKeep.Id).ToList();
            _dbContext.CompanySettings.RemoveRange(toDelete);
            await _dbContext.SaveChangesAsync();
            
            // Update the one we're keeping
            if (settings.Id == 0 || settings.Id != toKeep.Id)
            {
                settings.Id = toKeep.Id;
            }
        }
        else if (existingSettings.Count == 1)
        {
            var existing = existingSettings[0];
            if (settings.Id == 0)
            {
                settings.Id = existing.Id;
            }
        }

        // Update or add the settings
        if (settings.Id > 0)
        {
            var existing = await _dbContext.CompanySettings.FindAsync(settings.Id);
            if (existing != null)
            {
                // Update existing
                existing.CompanyName = settings.CompanyName;
                existing.CompanyAddress = settings.CompanyAddress;
                existing.TaxId = settings.TaxId;
                existing.FederalTaxPercent = settings.FederalTaxPercent;
                existing.StateTaxPercent = settings.StateTaxPercent;
                existing.SocialSecurityPercent = settings.SocialSecurityPercent;
                existing.MedicarePercent = settings.MedicarePercent;
                existing.PayPeriodsPerYear = settings.PayPeriodsPerYear;
                existing.DefaultHoursPerPeriod = settings.DefaultHoursPerPeriod;
            }
            else
            {
                _dbContext.CompanySettings.Add(settings);
            }
        }
        else
        {
            _dbContext.CompanySettings.Add(settings);
        }

        await _dbContext.SaveChangesAsync();
        
        // Invalidate cache so next request gets fresh data
        lock (_lockObject)
        {
            _cachedSettings = null;
        }
    }

    /// <summary>
    /// Invalidates the cache, forcing the next request to reload from database.
    /// Call this after settings are changed externally.
    /// </summary>
    public void InvalidateCache()
    {
        lock (_lockObject)
        {
            _cachedSettings = null;
        }
    }
}
