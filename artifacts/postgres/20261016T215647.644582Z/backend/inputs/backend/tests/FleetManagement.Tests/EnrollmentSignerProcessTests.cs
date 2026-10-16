using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FleetManagement.Enrollment;
using Probe = FleetManagement.SignerProbe.SignerProbe;
using Xunit;

namespace FleetManagement.Tests;

public sealed class EnrollmentSignerProcessTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FreshProcessRecoversPublishedCertificateAfterExitOrTermination(bool terminate)
    {
        var directory = Path.Combine(Path.GetTempPath(), "fleet-signer-process-" + Guid.NewGuid().ToString("N"));
        using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var deviceKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var now = DateTimeOffset.UtcNow;
        var rootRequest = new CertificateRequest("CN=Process test root", rootKey, HashAlgorithmName.SHA256);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(now.AddDays(-1), now.AddDays(7));
        var request = new EnrollmentSigningRequest(Guid.NewGuid(), Guid.NewGuid(), new string('a', 64),
            Convert.ToBase64String(deviceKey.ExportSubjectPublicKeyInfo()));
        var privateBytes = rootKey.ExportPkcs8PrivateKey();
        var input = new Probe.Input(directory, Convert.ToBase64String(root.RawData), Convert.ToBase64String(privateBytes), request);
        CryptographicOperations.ZeroMemory(privateBytes);
        try
        {
            var original = await RunProbe(input, terminate ? "issue-hold" : "issue", terminate);
            var recovered = await RunProbe(input, "lookup", false);
            var repeated = await RunProbe(input, "issue", false);
            Assert.Equal(request, recovered.Request);
            Assert.Equal(original.CertificateDer, recovered.CertificateDer);
            Assert.Equal(original.CertificateDer, repeated.CertificateDer);
            Assert.Empty(recovered.IntermediateCertificatesDer);
            EnrollmentCertificateVerifier.Verify(request.DeviceId, request.SubjectPublicKeyInfoBase64,
                recovered.CertificateDer, recovered.IntermediateCertificatesDer, root, DateTimeOffset.UtcNow, TimeSpan.FromDays(2));
            Assert.Single(Directory.GetFiles(directory, "*.reserved.json"));
            Assert.Single(Directory.GetFiles(directory, "*.issued.json"));
            Assert.Empty(Directory.GetFiles(directory, "*.partial.json"));
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<SignedEnrollmentCertificate> RunProbe(Probe.Input input, string mode, bool terminate)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[] { "exec", "--runtimeconfig",
                     Path.ChangeExtension(typeof(EnrollmentSignerProcessTests).Assembly.Location, ".runtimeconfig.json"),
                     "--depsfile", Path.ChangeExtension(typeof(EnrollmentSignerProcessTests).Assembly.Location, ".deps.json"),
                     typeof(Probe).Assembly.Location, mode })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start signer probe.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var errors = process.StandardError.ReadToEndAsync(deadline.Token);
        try
        {
            await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input).AsMemory(), deadline.Token);
            process.StandardInput.Close();
            var output = await process.StandardOutput.ReadLineAsync(deadline.Token);
            if (terminate)
            {
                Assert.False(process.HasExited);
                process.Kill(entireProcessTree: true);
            }
            await process.WaitForExitAsync(deadline.Token);
            Assert.Equal(string.Empty, await errors);
            if (!terminate)
                Assert.Equal(0, process.ExitCode);
            Assert.NotNull(output);
            return JsonSerializer.Deserialize<SignedEnrollmentCertificate>(output)
                ?? throw new InvalidOperationException("Signer probe returned no result.");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}