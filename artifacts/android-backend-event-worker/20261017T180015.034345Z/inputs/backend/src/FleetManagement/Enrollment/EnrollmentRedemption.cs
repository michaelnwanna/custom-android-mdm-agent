namespace FleetManagement.Enrollment;

public enum EnrollmentRedemptionStatus { Unauthorized, Conflict, Created, Recovered, Unavailable }

public sealed record EnrollmentRedemption(EnrollmentRedemptionStatus Status, EnrollmentCertificateResponse? Response = null);

public interface IEnrollmentRedemption
{
    Task<EnrollmentRedemption> RedeemAsync(EnrollmentHttpRequest request, string token, CancellationToken cancellationToken);
}

public sealed class EnrollmentRedemptionService(PostgresEnrollmentClaims claims,
    EnrollmentSigningCoordinator coordinator, ReadOnlyMemory<byte> digestKey) : IEnrollmentRedemption
{
    public async Task<EnrollmentRedemption> RedeemAsync(EnrollmentHttpRequest request, string token,
        CancellationToken cancellationToken)
    {
        var claim = await claims.ClaimAsync(request.PendingEnrollmentId, token, request.CsrDer, digestKey, cancellationToken);
        if (claim.Status == EnrollmentClaimStatus.Unauthorized)
            return new EnrollmentRedemption(EnrollmentRedemptionStatus.Unauthorized);
        if (claim.Status == EnrollmentClaimStatus.Conflict)
            return new EnrollmentRedemption(EnrollmentRedemptionStatus.Conflict);
        if (claim.Status == EnrollmentClaimStatus.Completed)
            return new EnrollmentRedemption(EnrollmentRedemptionStatus.Recovered, claim.Response);
        var response = await coordinator.RunAsync(request.PendingEnrollmentId, claim.OperationId!.Value, cancellationToken);
        return response is null
            ? new EnrollmentRedemption(EnrollmentRedemptionStatus.Unavailable)
            : new EnrollmentRedemption(claim.Status == EnrollmentClaimStatus.Claimed
                ? EnrollmentRedemptionStatus.Created : EnrollmentRedemptionStatus.Recovered, response);
    }
}