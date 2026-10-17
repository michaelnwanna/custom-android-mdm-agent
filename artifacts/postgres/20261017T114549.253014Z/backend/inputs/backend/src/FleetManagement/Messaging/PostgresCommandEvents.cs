using System.Data;
using System.Text.Json;
using FleetManagement.Enrollment;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FleetManagement.Messaging;

public sealed class CommandEventOwnership
{
    public Guid CommandId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid EnrollmentId { get; private set; }

    private CommandEventOwnership() { }

    internal static CommandEventOwnership Create(Guid commandId, Guid deviceId, Guid correlationId, Guid enrollmentId)
    {
        if (commandId == Guid.Empty || deviceId == Guid.Empty || correlationId == Guid.Empty)
            throw new ArgumentException("Invalid command ownership.");
        return new CommandEventOwnership { CommandId = commandId, DeviceId = deviceId,
            CorrelationId = correlationId, EnrollmentId = enrollmentId };
    }
}

public sealed class StoredCommandEvent
{
    public Guid EventId { get; private set; }
    public Guid CommandId { get; private set; }
    public Guid DeviceId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string ContentJson { get; private set; } = "";
    public string AcknowledgementJson { get; private set; } = "";

    private StoredCommandEvent() { }

    internal static StoredCommandEvent Create(CommandStateEvent message, string content, string acknowledgement) =>
        new() { EventId = message.EventId, CommandId = message.CommandId, DeviceId = message.DeviceId,
            CorrelationId = message.CorrelationId, ContentJson = content, AcknowledgementJson = acknowledgement };
}

public sealed record DurableCommandEventReceipt(string AcknowledgementJson, bool Duplicate)
{
    public override string ToString() => "DurableCommandEventReceipt { [redacted] }";
}

public sealed class PostgresCommandEvents(IDbContextFactory<FleetDbContext> contexts, TimeProvider clock)
{
    public async Task RegisterOwnershipAsync(Guid commandId, Guid deviceId, Guid correlationId,
        CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var enrollment = await EnabledEnrollment(context, deviceId, cancellationToken);
        var existing = await context.CommandEventOwnerships.SingleOrDefaultAsync(value => value.CommandId == commandId, cancellationToken);
        if (existing is not null)
        {
            if (existing.DeviceId != deviceId || existing.CorrelationId != correlationId || existing.EnrollmentId != enrollment.Id)
                throw new InvalidOperationException("Command ownership conflict.");
        }
        else
        {
            context.CommandEventOwnerships.Add(CommandEventOwnership.Create(commandId, deviceId, correlationId, enrollment.Id));
            await Save(context, cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<DurableCommandEventReceipt> IngestAsync(ReadOnlyMemory<byte> body, Guid authenticatedDeviceId,
        CancellationToken cancellationToken = default)
    {
        var message = CommandStateEvent.Parse(body, authenticatedDeviceId);
        var content = JsonSerializer.Serialize(message, JsonSerializerOptions.Web);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var enrollment = await EnabledEnrollment(context, authenticatedDeviceId, cancellationToken);
        var ownership = await context.CommandEventOwnerships.AsNoTracking()
            .SingleOrDefaultAsync(value => value.CommandId == message.CommandId, cancellationToken);
        if (ownership is null || ownership.DeviceId != authenticatedDeviceId || ownership.CorrelationId != message.CorrelationId ||
            ownership.EnrollmentId != enrollment.Id)
            throw new InvalidOperationException("Command ownership rejected.");
        var existing = await context.StoredCommandEvents.AsNoTracking()
            .SingleOrDefaultAsync(value => value.EventId == message.EventId, cancellationToken);
        if (existing is not null)
        {
            if (existing.ContentJson != content)
                throw new InvalidOperationException("Event identity conflict.");
            await transaction.CommitAsync(cancellationToken);
            return new DurableCommandEventReceipt(existing.AcknowledgementJson, true);
        }
        var acknowledgement = JsonSerializer.Serialize(new { schemaVersion = 1, deviceId = message.DeviceId,
            eventId = message.EventId, receivedAt = clock.GetUtcNow() }, JsonSerializerOptions.Web);
        context.StoredCommandEvents.Add(StoredCommandEvent.Create(message, content, acknowledgement));
        await Save(context, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new DurableCommandEventReceipt(acknowledgement, false);
    }

    private static async Task<PendingEnrollment> EnabledEnrollment(FleetDbContext context, Guid deviceId,
        CancellationToken cancellationToken)
    {
        var rows = await context.PendingEnrollments.FromSqlInterpolated(
            $"SELECT * FROM \"PendingEnrollments\" WHERE \"DeviceId\" = {deviceId} FOR UPDATE")
            .AsTracking().ToListAsync(cancellationToken);
        var enrollment = rows.SingleOrDefault();
        if (enrollment?.CompletedResponseJson is null || enrollment.DeviceAccessDisabled)
            throw new InvalidOperationException("Device enrollment rejected.");
        return enrollment;
    }

    private static async Task Save(FleetDbContext context, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException failure) when (failure.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new InvalidOperationException("Event or command identity conflict.");
        }
    }
}