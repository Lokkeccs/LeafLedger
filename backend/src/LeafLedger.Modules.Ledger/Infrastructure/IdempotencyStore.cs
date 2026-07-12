using System.Security.Cryptography;
using System.Text.Json;
using LeafLedger.Modules.Ledger.Application.Posting;
using LeafLedger.SharedKernel;
using Npgsql;

namespace LeafLedger.Modules.Ledger.Infrastructure;

public static class IdempotencyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<IdempotencyRecord?> FindLiveAsync(
        NpgsqlTransaction transaction,
        Guid spaceId,
        Guid key,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT request_hash, response_status, response_body::text " +
            "FROM idempotency_keys " +
            "WHERE space_id = @space AND idempotency_key = @key " +
            "AND created_at >= now() - interval '24 hours';",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("key", key);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new IdempotencyRecord(
            (byte[])reader[0],
            reader.GetInt32(1),
            SerializeResponse(JsonSerializer.Deserialize<PostingResponse>(reader.GetString(2), JsonOptions)!));
    }

    public static async Task InsertAsync(
        NpgsqlTransaction transaction,
        Guid spaceId,
        Guid key,
        Guid actorId,
        string target,
        byte[] requestHash,
        int responseStatus,
        string responseBody,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "INSERT INTO idempotency_keys " +
            "(space_id, idempotency_key, actor_id, target, request_hash, response_status, response_body) " +
            "VALUES (@space, @key, @actor, @target, @hash, @status, @body::jsonb);",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("actor", actorId);
        command.Parameters.AddWithValue("target", target);
        command.Parameters.AddWithValue("hash", requestHash);
        command.Parameters.AddWithValue("status", responseStatus);
        command.Parameters.AddWithValue("body", responseBody);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static async Task DeleteExpiredAsync(
        NpgsqlTransaction transaction,
        Guid spaceId,
        Guid key,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT delete_expired_idempotency_key(@space, @key);",
            transaction.Connection,
            transaction);
        command.Parameters.AddWithValue("space", spaceId);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public static bool IsSameRequest(IdempotencyRecord record, byte[] requestHash) =>
        CryptographicOperations.FixedTimeEquals(record.RequestHash, requestHash);

    public static Guid ParseKey(string value)
    {
        if (!Ulid.TryParse(value, out var ulid))
        {
            throw new FormatException("The idempotency key is not a valid ULID.");
        }

        return Id<IdempotencyKeyTag>.FromUlid(ulid).ToStorage();
    }

    public static byte[] Hash(PostJournalEntryCommand command) =>
        HashCanonical("post", new
        {
            date = command.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            description = command.Description.Trim(),
            reference = command.Reference?.Trim(),
            lines = command.Lines.Select(CanonicalLine).OrderBy(line => line, StringComparer.Ordinal).ToArray(),
        });

    public static byte[] Hash(ReverseJournalEntryCommand command) =>
        HashCanonical($"reverse:{command.EntryId:D}", new
        {
            date = command.Date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        });

    public static string SerializeResponse(object response) => JsonSerializer.Serialize(response, JsonOptions);

    private static string CanonicalLine(PostJournalLineRequest line) =>
        JsonSerializer.Serialize(new
        {
            accountId = line.AccountId.ToString("D"),
            amountMinor = line.AmountMinor,
            currency = line.Currency?.Trim().ToUpperInvariant(),
            baseAmountMinor = line.BaseAmountMinor,
            fxRate = line.FxRate?.Trim(),
            vatCodeId = line.VatCodeId?.ToString("D"),
            businessPartnerId = line.BusinessPartnerId?.ToString("D"),
            projectId = line.ProjectId?.ToString("D"),
            attributions = line.Attributions?.OrderBy(item => item.UserId).ThenBy(item => item.SharePermille)
                .Select(item => new { userId = item.UserId.ToString("D"), item.SharePermille }).ToArray(),
        }, JsonOptions);

    private static byte[] HashCanonical(string target, object payload)
    {
        var canonical = target + "\n" + JsonSerializer.Serialize(payload, JsonOptions);
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(canonical));
    }
}

public sealed record IdempotencyRecord(byte[] RequestHash, int ResponseStatus, string ResponseBody);

internal readonly struct IdempotencyKeyTag : IEntityTag
{
    public static string Prefix => "idk";
}