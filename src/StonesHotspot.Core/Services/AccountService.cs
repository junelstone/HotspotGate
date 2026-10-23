using Microsoft.EntityFrameworkCore;
using StonesHotspot.Core.Data;
using StonesHotspot.Core.Data.Entities;
using StonesHotspot.Core.Money;
using StonesHotspot.Core.Rules;
using StonesHotspot.Core.Security;

namespace StonesHotspot.Core.Services;

/// <summary>
/// Customer accounts. An account holds a balance; its tariff's package is bought from the balance when the
/// customer logs in or when the running package is used up (see <see cref="TryBuyPackageAsync"/>).
/// </summary>
public sealed class AccountService(HotspotDbContext db, IPasswordHasher hasher, CashService cash, AuditService audit, TimeProvider clock)
{
    public const int MinPasswordLength = 4;

    public Task<UserAccount?> FindAsync(int id, CancellationToken ct = default) =>
        db.UserAccounts.Include(a => a.Tariff).FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<UserAccount?> FindByUsernameAsync(string username, CancellationToken ct = default) =>
        db.UserAccounts.Include(a => a.Tariff).FirstOrDefaultAsync(a => a.Username == username.Trim(), ct);

    public async Task<UserAccount> CreateAsync(UserAccount account, string password, Staff? actor, CancellationToken ct = default)
    {
        account.Username = account.Username.Trim();
        if (account.Username.Length is 0 or > 64)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Username is required (max 64 characters).");
        if (password.Length < MinPasswordLength)
            throw new BusinessRuleException(ErrorCodes.WeakPassword, $"Password must have at least {MinPasswordLength} characters.");
        if (account.AllowedOverdraft.IsNegative)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Overdraft cannot be negative.");
        if (!await db.Tariffs.AnyAsync(t => t.Id == account.TariffId, ct))
            throw new BusinessRuleException(ErrorCodes.NotFound, $"Tariff {account.TariffId} not found.");
        if (await db.UserAccounts.AnyAsync(a => a.Username == account.Username, ct))
            throw new BusinessRuleException(ErrorCodes.DuplicateName, $"Username '{account.Username}' is taken.");

        account.PasswordHash = hasher.Hash(password);
        account.CreatedAt = clock.GetUtcNow().UtcDateTime;
        db.UserAccounts.Add(account);
        audit.Record(actor, "Account.Created", account.Username);
        await db.SaveChangesAsync(ct);
        return account;
    }

    /// <summary>Accounts whose username, name or phone contains <paramref name="search"/> (all when empty).</summary>
    public Task<List<UserAccount>> ListAsync(string? search = null, int take = 200, CancellationToken ct = default)
    {
        var query = db.UserAccounts.Include(a => a.Tariff).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = $"%{search.Trim()}%";
            query = query.Where(a => EF.Functions.Like(a.Username, term) || EF.Functions.Like(a.FirstName ?? "", term)
                                     || EF.Functions.Like(a.LastName ?? "", term) || EF.Functions.Like(a.Phone ?? "", term));
        }
        return query.OrderBy(a => a.Username).Take(take).ToListAsync(ct);
    }

    /// <summary>Changes the editable details of an account (not its balance or password).</summary>
    public async Task<UserAccount> UpdateAsync(int accountId, UserAccount changes, Staff? actor, CancellationToken ct = default)
    {
        var account = await FindAsync(accountId, ct) ?? throw NotFound(accountId);
        if (changes.AllowedOverdraft.IsNegative)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Overdraft cannot be negative.");
        if (!await db.Tariffs.AnyAsync(t => t.Id == changes.TariffId, ct))
            throw new BusinessRuleException(ErrorCodes.NotFound, $"Tariff {changes.TariffId} not found.");

        account.FirstName = changes.FirstName;
        account.LastName = changes.LastName;
        account.Phone = changes.Phone;
        account.TariffId = changes.TariffId;
        account.ExpiresAt = changes.ExpiresAt;
        account.AllowedOverdraft = changes.AllowedOverdraft;
        account.Enabled = changes.Enabled;
        audit.Record(actor, "Account.Updated", account.Username);
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task SetPasswordAsync(int accountId, string password, Staff? actor, CancellationToken ct = default)
    {
        if (password.Length < MinPasswordLength)
            throw new BusinessRuleException(ErrorCodes.WeakPassword, $"Password must have at least {MinPasswordLength} characters.");
        var account = await FindAsync(accountId, ct) ?? throw NotFound(accountId);
        account.PasswordHash = hasher.Hash(password);
        audit.Record(actor, "Account.PasswordChanged", account.Username);
        await db.SaveChangesAsync(ct);
    }

    public async Task SetEnabledAsync(int accountId, bool enabled, Staff? actor, CancellationToken ct = default)
    {
        var account = await FindAsync(accountId, ct) ?? throw NotFound(accountId);
        account.Enabled = enabled;
        audit.Record(actor, enabled ? "Account.Enabled" : "Account.Disabled", account.Username);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Adds money to the account balance and records the deposit. Returns the receipt number.</summary>
    public async Task<string> DepositAsync(int accountId, Xaf amount, PaymentMethod method, Staff staff, CancellationToken ct = default)
    {
        if (amount <= Xaf.Zero)
            throw new BusinessRuleException(ErrorCodes.Invalid, "Deposit must be greater than zero.");
        if (method == PaymentMethod.AccountBalance)
            throw new BusinessRuleException(ErrorCodes.Invalid, "A deposit cannot be paid from the account balance.");
        var account = await FindAsync(accountId, ct) ?? throw NotFound(accountId);

        account.Balance += amount;
        var receipt = await cash.NextReceiptNumberAsync(ct);
        await cash.AddTransactionAsync(TransactionType.AccountDeposit, amount, method, staff, receipt, ct,
            t => { t.UserAccountId = account.Id; t.Description = account.Username; });
        audit.Record(staff, "Account.Deposit", $"{account.Username}: {amount}");
        await db.SaveChangesAsync(ct);
        return receipt;
    }

    internal bool VerifyPassword(UserAccount account, string password) => hasher.Verify(password, account.PasswordHash);

    /// <summary>
    /// Buys one package of the account's tariff from its balance (balance may go down to −overdraft) and
    /// starts it. Not saved. Returns false if the balance is too low.
    /// </summary>
    internal async Task<bool> TryBuyPackageAsync(UserAccount account, CancellationToken ct)
    {
        var price = account.Tariff.Price;
        if (account.Balance - price < -account.AllowedOverdraft)
            return false;

        account.Balance -= price;
        AllowanceRules.Start(account.Allowance, account.Tariff, clock.GetUtcNow().UtcDateTime);
        var receipt = await cash.NextReceiptNumberAsync(ct);
        await cash.AddTransactionAsync(TransactionType.AccountCharge, price, PaymentMethod.AccountBalance, null, receipt, ct,
            t => { t.UserAccountId = account.Id; t.Description = account.Tariff.Name; });
        return true;
    }

    private static BusinessRuleException NotFound(int id) => new(ErrorCodes.NotFound, $"Account {id} not found.");
}
