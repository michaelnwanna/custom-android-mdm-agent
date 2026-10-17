using System.Security.Cryptography.X509Certificates;
using FleetManagement.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FleetManagement.Enrollment;

public interface IDeviceCertificateIdentity
{
    Task<Guid?> AuthenticateAsync(X509Certificate2 certificate, CancellationToken cancellationToken);
}

public sealed class DeviceCertificateIdentity(IDbContextFactory<FleetDbContext> factory,
    X509Certificate2 trustedRoot, TimeProvider clock) : IDeviceCertificateIdentity
{
    public async Task<Guid?> AuthenticateAsync(X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        try
        {
            var deviceId = DeviceCertificateProof.ReadClaimedDeviceId(certificate);
            await using var context = await factory.CreateDbContextAsync(cancellationToken);
            var enrollment = await context.PendingEnrollments.AsNoTracking().SingleOrDefaultAsync(
                value => value.DeviceId == deviceId && !value.DeviceAccessDisabled && value.CompletedResponseJson != null,
                cancellationToken);
            if (enrollment?.CompletedResponse is not { } response
                || response.CertificateDer != Convert.ToBase64String(certificate.RawData))
                return null;
            EnrollmentCertificateVerifier.Verify(deviceId, enrollment.SubjectPublicKeyInfoBase64!,
                response.CertificateDer, response.IntermediateCertificatesDer, trustedRoot,
                clock.GetUtcNow(), TimeSpan.FromDays(2));
            return deviceId;
        }
        catch (InvalidEnrollmentCertificateException)
        {
            return null;
        }
    }
}