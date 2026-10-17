using System.Text;
using FleetManagement.Enrollment;
using Xunit;

namespace FleetManagement.Tests;

public sealed class EnrollmentHttpRequestTests
{
    private const string PendingId = "11111111-1111-4111-8111-111111111111";
    private static readonly string Csr = Convert.ToBase64String(new byte[48]);
    private static string Valid => $$"""{"schemaVersion":1,"pendingEnrollmentId":"{{PendingId}}","csrDer":"{{Csr}}"}""";

    [Fact]
    public void ValidShapeIsDecodedWithoutClaimingCryptographicValidation()
    {
        var request = EnrollmentHttpRequest.Parse(Encoding.UTF8.GetBytes(Valid));
        Assert.Equal(Guid.Parse(PendingId), request.PendingEnrollmentId);
        Assert.Equal(Csr, request.CsrDer);
        Assert.DoesNotContain(Csr, request.ToString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    public void MalformedOrIncompleteBodiesAreRejected(string body)
        => Assert.Throws<ArgumentException>(() => EnrollmentHttpRequest.Parse(Encoding.UTF8.GetBytes(body)));

    [Theory]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":\"1\"")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":2")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"schema\\u0056ersion\":1")]
    [InlineData("\"schemaVersion\":1", "\"schemaVersion\":1,\"extra\":true")]
    [InlineData("\"pendingEnrollmentId\"", "\"PendingEnrollmentId\"")]
    [InlineData("11111111-1111-4111-8111-111111111111", "{11111111-1111-4111-8111-111111111111}")]
    [InlineData("\"csrDer\":", "\"csrDer\":null,\"other\":")]
    public void ContractViolationsAreRejected(string original, string replacement)
        => Assert.Throws<ArgumentException>(() => EnrollmentHttpRequest.Parse(Encoding.UTF8.GetBytes(Valid.Replace(original, replacement))));

    [Fact]
    public void OversizedBodyAndWhitespaceBase64AreRejected()
    {
        Assert.Throws<ArgumentException>(() => EnrollmentHttpRequest.Parse(new byte[EnrollmentHttpRequest.MaximumBodyBytes + 1]));
        Assert.Throws<ArgumentException>(() => EnrollmentHttpRequest.Parse(Encoding.UTF8.GetBytes(Valid.Replace(Csr, Csr + " "))));
    }
}