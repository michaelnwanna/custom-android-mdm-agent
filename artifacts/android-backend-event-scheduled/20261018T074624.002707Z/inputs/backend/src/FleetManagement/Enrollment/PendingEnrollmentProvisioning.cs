using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Enrollment;

public sealed record EnrollmentAdministrator(string Issuer, string Subject);

public sealed record PendingEnrollmentProvisioned(int SchemaVersion, Guid PendingEnrollmentId,
    string BootstrapToken, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt)
{
    public override string ToString() => "PendingEnrollmentProvisioned { [redacted] }";
}

public sealed record EnrollmentProvisioningAudit(Guid PendingEnrollmentId, string Issuer,
    string Subject, DateTimeOffset CreatedAt);

public interface IPendingEnrollmentProvisioner
{
    Task<PendingEnrollmentProvisioned> CreateAsync(EnrollmentAdministrator administrator,
        TimeSpan lifetime, CancellationToken cancellationToken);
}

public sealed class PendingEnrollmentProvisioner(IDbContextFactory<FleetDbContext> contexts,
    TimeProvider clock, ReadOnlyMemory<byte> digestKey) : IPendingEnrollmentProvisioner
{
    public async Task<PendingEnrollmentProvisioned> CreateAsync(EnrollmentAdministrator administrator,
        TimeSpan lifetime, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(administrator);
        if (!Uri.TryCreate(administrator.Issuer, UriKind.Absolute, out var issuer) ||
            issuer.Scheme != Uri.UriSchemeHttps || administrator.Issuer.Length > 2048 ||
            string.IsNullOrWhiteSpace(administrator.Subject) || administrator.Subject.Length > 256 ||
            lifetime < TimeSpan.FromMinutes(1) || lifetime > EnrollmentBootstrapTokens.MaximumLifetime)
            throw new ArgumentException("Invalid pending enrollment provisioning request.");
        var issued = new EnrollmentBootstrapTokens(clock).Issue(Guid.NewGuid(), digestKey.Span, lifetime);
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        context.PendingEnrollments.Add(PendingEnrollment.Create(issued.Credential));
        context.EnrollmentProvisioningAudits.Add(new EnrollmentProvisioningAudit(
            issued.Credential.PendingEnrollmentId, administrator.Issuer, administrator.Subject, issued.Credential.IssuedAt));
        await context.SaveChangesAsync(cancellationToken);
        return new PendingEnrollmentProvisioned(1, issued.Credential.PendingEnrollmentId, issued.Secret,
            issued.Credential.IssuedAt, issued.Credential.ExpiresAt);
    }
}