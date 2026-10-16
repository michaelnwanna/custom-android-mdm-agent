using System.Text.Json;

namespace FleetManagement.Enrollment;

public sealed record EnrollmentHttpRequest(Guid PendingEnrollmentId, string CsrDer)
{
    public const int MaximumBodyBytes = 24576;

    public static EnrollmentHttpRequest Parse(ReadOnlyMemory<byte> body)
    {
        if (body.Length > MaximumBodyBytes)
            throw new ArgumentException("Invalid enrollment request.");
        try
        {
            using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Invalid enrollment request.");
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!properties.Add(property.Name) || property.Name is not
                    ("schemaVersion" or "pendingEnrollmentId" or "csrDer"))
                    throw new ArgumentException("Invalid enrollment request.");
            if (properties.Count != 3 ||
                !root.GetProperty("schemaVersion").TryGetInt32(out var version) || version != 1 ||
                !Guid.TryParseExact(root.GetProperty("pendingEnrollmentId").GetString(), "D", out var pendingId))
                throw new ArgumentException("Invalid enrollment request.");
            var csr = root.GetProperty("csrDer").GetString();
            if (csr is null || csr.Length is < 64 or > 16384 || csr.Length % 4 != 0 ||
                csr.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '+' or '/' or '=')))
                throw new ArgumentException("Invalid enrollment request.");
            Span<byte> decoded = stackalloc byte[12288];
            if (!Convert.TryFromBase64String(csr, decoded, out var written) ||
                Convert.ToBase64String(decoded[..written]) != csr)
                throw new ArgumentException("Invalid enrollment request.");
            return new EnrollmentHttpRequest(pendingId, csr);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new ArgumentException("Invalid enrollment request.");
        }
    }

    public override string ToString() => "EnrollmentHttpRequest { [redacted] }";
}