using System.Security.Cryptography.X509Certificates;

namespace FleetManagement.Enrollment;

public sealed record EnrollmentSigningRequest(Guid OperationId, Guid DeviceId, string CsrSha256,
    string SubjectPublicKeyInfoBase64);

public sealed record SignedEnrollmentCertificate(EnrollmentSigningRequest Request, string CertificateDer,
    IReadOnlyList<string> IntermediateCertificatesDer);

public enum EnrollmentSignerState { Unknown, NotFound, Issued }

public sealed record EnrollmentSignerLookup(EnrollmentSignerState State, SignedEnrollmentCertificate? Certificate = null);

public interface IEnrollmentSigner
{
    Task<EnrollmentSignerLookup> LookupAsync(EnrollmentSigningRequest request, CancellationToken cancellationToken);
    Task<SignedEnrollmentCertificate> IssueAsync(EnrollmentSigningRequest request, CancellationToken cancellationToken);
}

public sealed record EnrollmentSigningWork(EnrollmentSigningRequest Request, EnrollmentCertificateResponse? Response);

public sealed class EnrollmentSigningCoordinator(PostgresEnrollmentClaims claims, IEnrollmentSigner signer,
    X509Certificate2 trustedRoot, TimeSpan maximumLifetime)
{
    public async Task<EnrollmentCertificateResponse?> RunAsync(Guid pendingId, Guid operationId,
        CancellationToken cancellationToken = default)
    {
        var work = await claims.ReadSigningWorkAsync(pendingId, operationId, cancellationToken);
        if (work.Response is not null)
            return work.Response;
        var lookup = await signer.LookupAsync(work.Request, cancellationToken);
        SignedEnrollmentCertificate issued;
        switch (lookup.State)
        {
            case EnrollmentSignerState.Unknown:
                return null;
            case EnrollmentSignerState.Issued:
                issued = lookup.Certificate ?? throw new InvalidOperationException("Signer result is missing.");
                break;
            case EnrollmentSignerState.NotFound:
                if (lookup.Certificate is not null)
                    throw new InvalidOperationException("Signer lookup result is inconsistent.");
                if (!await claims.TryBeginSigningAsync(pendingId, operationId, cancellationToken))
                    return null;
                issued = await signer.IssueAsync(work.Request, cancellationToken);
                break;
            default:
                throw new InvalidOperationException("Signer lookup state is unsupported.");
        }
        if (issued.Request != work.Request)
            throw new InvalidOperationException("Signer result does not match its operation.");
        return await claims.CompleteAsync(pendingId, operationId, issued.CertificateDer,
            issued.IntermediateCertificatesDer, trustedRoot, maximumLifetime, cancellationToken);
    }
}