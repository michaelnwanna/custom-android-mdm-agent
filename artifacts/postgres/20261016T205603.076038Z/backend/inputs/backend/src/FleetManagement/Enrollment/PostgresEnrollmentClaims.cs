using System.Data;
using System.Security.Cryptography.X509Certificates;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Enrollment;

public sealed class PostgresEnrollmentClaims(IDbContextFactory<FleetDbContext> contexts, TimeProvider clock)
{
    public async Task<EnrollmentCertificateResponse> CompleteAsync(Guid pendingId, Guid operationId,
        string certificateDer, IReadOnlyList<string> intermediateCertificatesDer,
        X509Certificate2 trustedRoot, TimeSpan maximumLifetime, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var records = await context.PendingEnrollments.FromSqlInterpolated(
            $"SELECT * FROM \"PendingEnrollments\" WHERE \"Id\" = {pendingId} FOR UPDATE")
            .AsTracking().ToListAsync(cancellationToken);
        var enrollment = records.SingleOrDefault();
        if (enrollment?.OperationId is null || enrollment.OperationId != operationId)
            throw new InvalidOperationException("Enrollment operation does not match its claim.");
        if (enrollment.CompletedResponse is { } existing)
            return existing;
        var response = EnrollmentCertificateVerifier.Verify(enrollment.DeviceId!.Value,
            enrollment.SubjectPublicKeyInfoBase64!, certificateDer, intermediateCertificatesDer,
            trustedRoot, clock.GetUtcNow(), maximumLifetime);
        var completed = enrollment.Complete(operationId, response);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return completed;
    }

    public async Task RegisterAsync(BootstrapTokenCredential credential, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.PendingEnrollments.Add(PendingEnrollment.Create(credential));
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<EnrollmentClaim> ClaimAsync(Guid pendingId, string? token, string? csrDer,
        ReadOnlyMemory<byte> digestKey, CancellationToken cancellationToken = default)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var records = await context.PendingEnrollments.FromSqlInterpolated(
            $"SELECT * FROM \"PendingEnrollments\" WHERE \"Id\" = {pendingId} FOR UPDATE")
            .AsTracking().ToListAsync(cancellationToken);
        var enrollment = records.SingleOrDefault();
        if (!new EnrollmentBootstrapTokens(clock).Verify(pendingId, token, enrollment?.Credential, digestKey.Span))
            return new EnrollmentClaim(EnrollmentClaimStatus.Unauthorized);

        var verified = EnrollmentCsrVerifier.Verify(csrDer);
        var now = clock.GetUtcNow();
        if (now < enrollment!.IssuedAt || now >= enrollment.ExpiresAt)
            return new EnrollmentClaim(EnrollmentClaimStatus.Unauthorized);

        var claim = enrollment.Claim(verified, now);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claim;
    }
}