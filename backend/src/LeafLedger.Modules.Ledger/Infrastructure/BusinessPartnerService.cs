using System.Data;
using System.Globalization;
using System.Text.Json;
using LeafLedger.Modules.Ledger.Application.MasterData;
using LeafLedger.Modules.Ledger.Infrastructure.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

internal sealed class BusinessPartnerService : IBusinessPartnerService
{
    private static readonly HashSet<string> ValidTypes = ["customer", "vendor", "both", "financial-services"];
    private readonly LedgerDbContext _db;
    private readonly IdempotencyMetrics _metrics;

    public BusinessPartnerService(LedgerDbContext db, IdempotencyMetrics metrics)
    {
        _db = db;
        _metrics = metrics;
    }

    public async Task<BusinessPartnerCatalogReport> GetBusinessPartnersAsync(Guid spaceId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await OpenBoundTransactionAsync(spaceId, Guid.Empty, cancellationToken).ConfigureAwait(false);
        var partners = await _db.Set<BusinessPartner>()
            .AsNoTracking()
            .OrderBy(partner => partner.Name)
            .ThenBy(partner => partner.Id)
            .Select(partner => new BusinessPartnerView(
                partner.Id,
                partner.Name,
                partner.PartnerNumber,
                partner.Type,
                partner.CountryCode,
                partner.IsActive,
                partner.ValidFrom,
                partner.ValidTo,
                partner.Notes,
                EF.Property<uint>(partner, "xmin").ToString(CultureInfo.InvariantCulture)))
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return new BusinessPartnerCatalogReport(spaceId, partners);
    }

    public async Task<BusinessPartnerView?> GetBusinessPartnerAsync(Guid spaceId, Guid partnerId, CancellationToken cancellationToken = default)
    {
        await using var transaction = await OpenBoundTransactionAsync(spaceId, Guid.Empty, cancellationToken).ConfigureAwait(false);
        var partner = await _db.Set<BusinessPartner>().SingleOrDefaultAsync(
            item => item.Id == partnerId && item.SpaceId == spaceId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return partner is null ? null : ToView(partner);
    }

    public Task<BusinessPartnerOutcome<BusinessPartnerView>> CreateAsync(Guid spaceId, Guid actorId, string idempotencyKey, CreateBusinessPartnerCommand command, CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, "partner.create", command, StatusCodes.Status201Created,
            async (_, ct) => await CreateWithinTransactionAsync(spaceId, actorId, command, ct).ConfigureAwait(false), cancellationToken);

    public Task<BusinessPartnerOutcome<BusinessPartnerView>> UpdateAsync(Guid spaceId, Guid actorId, Guid partnerId, string idempotencyKey, UpdateBusinessPartnerCommand command, CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, $"partner.update:{partnerId:D}", command, StatusCodes.Status200OK,
            async (_, ct) => await UpdateWithinTransactionAsync(spaceId, partnerId, command, ct).ConfigureAwait(false), cancellationToken);

    public Task<BusinessPartnerOutcome<BusinessPartnerView>> DeleteAsync(Guid spaceId, Guid actorId, Guid partnerId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        ExecuteAsync(spaceId, actorId, idempotencyKey, $"partner.delete:{partnerId:D}", new { partnerId }, StatusCodes.Status204NoContent,
            async (_, ct) => await DeleteWithinTransactionAsync(spaceId, partnerId, ct).ConfigureAwait(false), cancellationToken);

    private async Task<BusinessPartnerOutcome<BusinessPartnerView>> CreateWithinTransactionAsync(Guid spaceId, Guid actorId, CreateBusinessPartnerCommand command, CancellationToken cancellationToken)
    {
        var validation = Validate(command.Name, command.Type, command.ValidFrom, command.ValidTo, command.PartnerNumber, command.CountryCode);
        if (validation is not null) return BusinessPartnerOutcome<BusinessPartnerView>.Failed(422, validation);

        var partner = new BusinessPartner
        {
            Id = Guid.NewGuid(), SpaceId = spaceId, CreatedBy = actorId, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
            Name = command.Name.Trim(), PartnerNumber = Normalize(command.PartnerNumber), Type = command.Type.Trim().ToLowerInvariant(),
            CountryCode = Normalize(command.CountryCode)?.ToUpperInvariant(), IsActive = command.IsActive, ValidFrom = command.ValidFrom, ValidTo = command.ValidTo, Notes = Normalize(command.Notes),
        };
        _db.Set<BusinessPartner>().Add(partner);
        return BusinessPartnerOutcome<BusinessPartnerView>.Success(ToView(partner), StatusCodes.Status201Created);
    }

    private async Task<BusinessPartnerOutcome<BusinessPartnerView>> UpdateWithinTransactionAsync(Guid spaceId, Guid partnerId, UpdateBusinessPartnerCommand command, CancellationToken cancellationToken)
    {
        var validation = Validate(command.Name, command.Type, command.ValidFrom, command.ValidTo, command.PartnerNumber, command.CountryCode);
        if (validation is not null) return BusinessPartnerOutcome<BusinessPartnerView>.Failed(422, validation);
        if (!uint.TryParse(command.Version, NumberStyles.None, CultureInfo.InvariantCulture, out var version))
            return BusinessPartnerOutcome<BusinessPartnerView>.Failed(400, new BusinessPartnerIssue("partner.version_required", "A valid partner version is required.", "version"));

        var partner = await _db.Set<BusinessPartner>().SingleOrDefaultAsync(item => item.Id == partnerId && item.SpaceId == spaceId, cancellationToken).ConfigureAwait(false);
        if (partner is null) return BusinessPartnerOutcome<BusinessPartnerView>.Failed(404, new BusinessPartnerIssue("partner.not_found", "The business partner does not exist in this space."));
        var current = ToView(partner);
        if (!string.Equals(current.Version, command.Version, StringComparison.Ordinal))
            return BusinessPartnerOutcome<BusinessPartnerView>.Failed(409, new BusinessPartnerIssue(
                "partner.version_conflict",
                "The business partner changed. Reload the current state before retrying.",
                Current: current));
        _db.Entry(partner).Property<uint>("xmin").OriginalValue = version;
        partner.Name = command.Name.Trim(); partner.PartnerNumber = Normalize(command.PartnerNumber); partner.Type = command.Type.Trim().ToLowerInvariant();
        partner.CountryCode = Normalize(command.CountryCode)?.ToUpperInvariant(); partner.IsActive = command.IsActive; partner.ValidFrom = command.ValidFrom; partner.ValidTo = command.ValidTo; partner.Notes = Normalize(command.Notes); partner.UpdatedAt = DateTimeOffset.UtcNow;
        return BusinessPartnerOutcome<BusinessPartnerView>.Success(ToView(partner));
    }

    private async Task<BusinessPartnerOutcome<BusinessPartnerView>> DeleteWithinTransactionAsync(Guid spaceId, Guid partnerId, CancellationToken cancellationToken)
    {
        var partner = await _db.Set<BusinessPartner>().SingleOrDefaultAsync(item => item.Id == partnerId && item.SpaceId == spaceId, cancellationToken).ConfigureAwait(false);
        if (partner is null) return BusinessPartnerOutcome<BusinessPartnerView>.Failed(404, new BusinessPartnerIssue("partner.not_found", "The business partner does not exist in this space."));
        if (await _db.JournalLines.AnyAsync(line => line.SpaceId == spaceId && line.BusinessPartnerId == partnerId, cancellationToken).ConfigureAwait(false))
            return BusinessPartnerOutcome<BusinessPartnerView>.Failed(409, new BusinessPartnerIssue("partner.in_use", "The business partner is referenced by a journal line."));
        _db.Set<BusinessPartner>().Remove(partner);
        return BusinessPartnerOutcome<BusinessPartnerView>.Success(ToView(partner), StatusCodes.Status204NoContent);
    }

    private async Task<BusinessPartnerOutcome<BusinessPartnerView>> ExecuteAsync(
        Guid spaceId, Guid actorId, string idempotencyKey, string target, object request, int successStatus,
        Func<NpgsqlTransaction, CancellationToken, Task<BusinessPartnerOutcome<BusinessPartnerView>>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await OpenBoundTransactionAsync(spaceId, actorId, cancellationToken).ConfigureAwait(false);
        Guid key;
        try { key = IdempotencyStore.ParseKey(idempotencyKey); }
        catch (FormatException) { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return BusinessPartnerOutcome<BusinessPartnerView>.Failed(400, new BusinessPartnerIssue("idempotency.key_invalid", "Idempotency-Key must be a valid ULID.")); }
        var requestHash = IdempotencyStore.HashPeriod(target, request);
        await AcquireKeyLockAsync(spaceId, key, transaction, cancellationToken).ConfigureAwait(false);
        var existing = await IdempotencyStore.FindLiveAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            if (IdempotencyStore.IsSameRequest(existing, requestHash)) return BusinessPartnerOutcome<BusinessPartnerView>.Replayed(new BusinessPartnerReplay(existing.ResponseStatus, existing.ResponseBody));
            _metrics.RecordCollision(spaceId);
            return BusinessPartnerOutcome<BusinessPartnerView>.Failed(409, new BusinessPartnerIssue("idempotency.key_reused", "The idempotency key was already used for a different request."));
        }
        await IdempotencyStore.DeleteExpiredAsync(transaction, spaceId, key, cancellationToken).ConfigureAwait(false);
        var outcome = await operation(transaction, cancellationToken).ConfigureAwait(false);
        if (!outcome.IsSuccess) { await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false); return outcome; }
        try
        {
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            var persistedValue = successStatus == StatusCodes.Status204NoContent
                ? outcome.Value!
                : ToView(await _db.Set<BusinessPartner>().SingleAsync(
                    partner => partner.Id == outcome.Value!.Id && partner.SpaceId == spaceId,
                    cancellationToken).ConfigureAwait(false));
                await IdempotencyStore.InsertAsync(transaction, spaceId, key, actorId, target, requestHash, successStatus, IdempotencyStore.SerializeResponse(persistedValue), cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return BusinessPartnerOutcome<BusinessPartnerView>.Success(persistedValue, successStatus);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                var current = target.StartsWith("partner.update:", StringComparison.Ordinal) &&
                              Guid.TryParse(target["partner.update:".Length..], out var partnerId)
                    ? await GetBusinessPartnerAsync(spaceId, partnerId, cancellationToken).ConfigureAwait(false)
                    : null;
                return BusinessPartnerOutcome<BusinessPartnerView>.Failed(409, new BusinessPartnerIssue(
                    "partner.version_conflict",
                    "The business partner changed. Reload the current state before retrying.",
                    Current: current));
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            var issue = postgres.ConstraintName?.Contains("partner_number", StringComparison.OrdinalIgnoreCase) == true
                ? new BusinessPartnerIssue("partner.number_taken", "The partner number is already used in this space.", "partnerNumber")
                : new BusinessPartnerIssue("partner.name_taken", "The partner name is already used in this space.", "name");
            return BusinessPartnerOutcome<BusinessPartnerView>.Failed(422, issue);
        }
    }

    private static BusinessPartnerIssue? Validate(string name, string type, DateOnly? validFrom, DateOnly? validTo, string? number, string? country)
    {
        if (string.IsNullOrWhiteSpace(name)) return new("partner.name_required", "Partner name is required.", "name");
        if (!ValidTypes.Contains(type.Trim().ToLowerInvariant())) return new("partner.type_invalid", "Partner type is invalid.", "type");
        if (validFrom is not null && validTo is not null && validFrom > validTo) return new("partner.validity_range_invalid", "The validity start must not be after the validity end.", "validFrom");
        if (country is not null && (country.Trim().Length != 2 || country.Trim().Any(character => !((character >= 'A' && character <= 'Z') || (character >= 'a' && character <= 'z'))))) return new("partner.country_invalid", "Country code must be an ISO alpha-2 value.", "countryCode");
        return null;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private BusinessPartnerView ToView(BusinessPartner partner) => new(
        partner.Id,
        partner.Name,
        partner.PartnerNumber,
        partner.Type,
        partner.CountryCode,
        partner.IsActive,
        partner.ValidFrom,
        partner.ValidTo,
        partner.Notes,
        _db.Entry(partner).Property<uint>("xmin").CurrentValue.ToString(CultureInfo.InvariantCulture));

    private async Task<NpgsqlTransaction> OpenBoundTransactionAsync(Guid spaceId, Guid actorId, CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        var transaction = (NpgsqlTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        _db.Database.UseTransaction(transaction);
        await using var command = new NpgsqlCommand("SET LOCAL ROLE leafledger_app; SELECT set_config('app.current_space_id', @space, true); SELECT set_config('app.current_actor', @actor, true);", connection, transaction);
        command.Parameters.AddWithValue("space", spaceId.ToString()); command.Parameters.AddWithValue("actor", actorId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return transaction;
    }

    private static async Task AcquireKeyLockAsync(Guid spaceId, Guid key, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@lock_key, 0));", transaction.Connection, transaction);
        command.Parameters.AddWithValue("lock_key", $"{spaceId:D}:{key:D}");
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}