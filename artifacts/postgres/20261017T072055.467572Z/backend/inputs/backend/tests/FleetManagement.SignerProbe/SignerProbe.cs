using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FleetManagement.Enrollment;

namespace FleetManagement.SignerProbe;

public static class SignerProbe
{
    public sealed record Input(string Ledger, string IssuerDer, string PrivateKeyPkcs8, EnrollmentSigningRequest Request);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var input = JsonSerializer.Deserialize<Input>(await Console.In.ReadToEndAsync())
                ?? throw new InvalidOperationException();
            var privateBytes = Convert.FromBase64String(input.PrivateKeyPkcs8);
            using var key = ECDsa.Create();
            try
            {
                key.ImportPkcs8PrivateKey(privateBytes, out _);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(privateBytes);
            }
            using var publicIssuer = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(input.IssuerDer));
            using var issuer = publicIssuer.CopyWithPrivateKey(key);
            var signer = new LabLedgerEnrollmentSigner(input.Ledger, issuer, TimeProvider.System, TimeSpan.FromDays(1));
            var result = args.Single() switch
            {
                "issue" or "issue-hold" => await signer.IssueAsync(input.Request, default),
                "lookup" => (await signer.LookupAsync(input.Request, default)).Certificate
                    ?? throw new InvalidOperationException(),
                _ => throw new InvalidOperationException()
            };
            Console.WriteLine(JsonSerializer.Serialize(result));
            await Console.Out.FlushAsync();
            if (args[0] == "issue-hold")
                await Task.Delay(Timeout.InfiniteTimeSpan);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Signer probe failed ({error.GetType().Name}); input and key material are redacted.");
            return 1;
        }
    }
}