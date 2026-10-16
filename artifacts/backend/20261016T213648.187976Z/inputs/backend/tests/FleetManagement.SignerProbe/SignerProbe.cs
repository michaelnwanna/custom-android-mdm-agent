using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FleetManagement.Enrollment;

namespace FleetManagement.SignerProbe;

public static class SignerProbe
{
    public sealed record Input(string Ledger, string IssuerPfx, EnrollmentSigningRequest Request);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var input = JsonSerializer.Deserialize<Input>(await Console.In.ReadToEndAsync())
                ?? throw new InvalidOperationException();
            var pfx = Convert.FromBase64String(input.IssuerPfx);
            using var issuer = X509CertificateLoader.LoadPkcs12(pfx, null, X509KeyStorageFlags.EphemeralKeySet);
            CryptographicOperations.ZeroMemory(pfx);
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