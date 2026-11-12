using System.Buffers.Binary;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.WebUtilities;

namespace backend.tests.Support;

/// <summary>
/// A test-only WebAuthn authenticator holding one ES256 passkey. It answers the options the API
/// returns with a real <c>none</c> attestation and real signed assertions, so tests exercise the
/// server library rather than a mock. Each knob breaks one thing a real authenticator or browser
/// would get right, for the negative cases.
/// </summary>
/// <remarks>
/// Shared with the integration tests by a linked compile item. It stands in for a browser and an
/// authenticator only; production code never builds or parses WebAuthn structures itself.
/// </remarks>
internal sealed class SoftwareAuthenticator : IDisposable
{
    private const byte UserPresent = 0x01;
    private const byte UserVerifiedFlag = 0x04;
    private const byte BackupEligibleFlag = 0x08;
    private const byte BackedUpFlag = 0x10;
    private const byte AttestedCredentialData = 0x40;

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public SoftwareAuthenticator(string origin = "http://localhost:3090")
    {
        Origin = origin;
    }

    /// <summary>The origin written into client data, as a browser would.</summary>
    public string Origin { get; set; }

    /// <summary>When set, signs for this RP ID instead of the one the options name.</summary>
    public string? RpIdOverride { get; set; }

    public bool UserVerified { get; set; } = true;

    /// <summary>A synced passkey (BE). Fixed when the credential is created.</summary>
    public bool BackupEligible { get; set; }

    public bool BackedUp { get; set; }

    /// <summary>The next signature counter. Synced passkeys commonly leave it at 0.</summary>
    public uint Counter { get; set; }

    /// <summary>How much each assertion advances <see cref="Counter"/>.</summary>
    public uint CounterStep { get; set; } = 1;

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(16);

    /// <summary>The user handle the registration options carried.</summary>
    public byte[]? UserHandle { get; private set; }

    /// <summary>Answers <c>navigator.credentials.create()</c> options with a <c>none</c> attestation.</summary>
    public JsonElement CreateAttestation(JsonElement publicKeyOptions, string? challengeOverride = null)
    {
        var challenge = challengeOverride ?? publicKeyOptions.GetProperty("challenge").GetString()!;
        var rpId = RpIdOverride ?? publicKeyOptions.GetProperty("rp").GetProperty("id").GetString()!;
        UserHandle = WebEncoders.Base64UrlDecode(publicKeyOptions.GetProperty("user").GetProperty("id").GetString()!);

        var clientData = ClientData("webauthn.create", challenge);
        var authData = AuthenticatorData(rpId, AttestedCredentialData, Counter, attested: true);

        var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
        attestation.WriteStartMap(3);
        attestation.WriteTextString("fmt");
        attestation.WriteTextString("none");
        attestation.WriteTextString("attStmt");
        attestation.WriteStartMap(0);
        attestation.WriteEndMap();
        attestation.WriteTextString("authData");
        attestation.WriteByteString(authData);
        attestation.WriteEndMap();

        return Credential(new JsonObject
        {
            ["clientDataJSON"] = Encode(clientData),
            ["attestationObject"] = Encode(attestation.Encode()),
            ["transports"] = new JsonArray("internal"),
        });
    }

    /// <summary>Answers <c>navigator.credentials.get()</c> options with a signed assertion.</summary>
    public JsonElement CreateAssertion(
        JsonElement publicKeyOptions,
        bool includeUserHandle = true,
        string? challengeOverride = null,
        byte[]? userHandleOverride = null,
        bool corruptSignature = false)
    {
        var challenge = challengeOverride ?? publicKeyOptions.GetProperty("challenge").GetString()!;
        var rpId = RpIdOverride ?? publicKeyOptions.GetProperty("rpId").GetString()!;

        Counter += CounterStep;
        var clientData = ClientData("webauthn.get", challenge);
        var authData = AuthenticatorData(rpId, 0, Counter, attested: false);

        var signature = _key.SignData(
            [.. authData, .. SHA256.HashData(clientData)],
            HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence);
        if (corruptSignature)
            signature[^1] ^= 0xFF;

        var response = new JsonObject
        {
            ["clientDataJSON"] = Encode(clientData),
            ["authenticatorData"] = Encode(authData),
            ["signature"] = Encode(signature),
        };
        var userHandle = userHandleOverride ?? UserHandle;
        if (includeUserHandle && userHandle is not null)
            response["userHandle"] = Encode(userHandle);

        return Credential(response);
    }

    public void Dispose() => _key.Dispose();

    private byte[] ClientData(string type, string challenge) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            type,
            challenge,
            origin = Origin,
            crossOrigin = false,
        }));

    private byte[] AuthenticatorData(string rpId, byte extraFlags, uint counter, bool attested)
    {
        var flags = (byte)(UserPresent | extraFlags);
        if (UserVerified)
            flags |= UserVerifiedFlag;
        if (BackupEligible)
            flags |= BackupEligibleFlag;
        if (BackedUp)
            flags |= BackedUpFlag;

        var data = new List<byte>();
        data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
        data.Add(flags);
        var counterBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(counterBytes, counter);
        data.AddRange(counterBytes);

        if (attested)
        {
            data.AddRange(new byte[16]); // AAGUID: not disclosed
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)CredentialId.Length);
            data.AddRange(length);
            data.AddRange(CredentialId);
            data.AddRange(CosePublicKey());
        }

        return [.. data];
    }

    private byte[] CosePublicKey()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
        cose.WriteStartMap(5);
        cose.WriteInt32(1);  // kty
        cose.WriteInt32(2);  // EC2
        cose.WriteInt32(3);  // alg
        cose.WriteInt32(-7); // ES256
        cose.WriteInt32(-1); // crv
        cose.WriteInt32(1);  // P-256
        cose.WriteInt32(-2); // x
        cose.WriteByteString(parameters.Q.X!);
        cose.WriteInt32(-3); // y
        cose.WriteByteString(parameters.Q.Y!);
        cose.WriteEndMap();
        return cose.Encode();
    }

    private JsonElement Credential(JsonObject response)
    {
        var id = Encode(CredentialId);
        var credential = new JsonObject
        {
            ["id"] = id,
            ["rawId"] = id,
            ["type"] = "public-key",
            ["response"] = response,
            ["clientExtensionResults"] = new JsonObject(),
        };
        return JsonDocument.Parse(credential.ToJsonString()).RootElement.Clone();
    }

    private static string Encode(byte[] bytes) => WebEncoders.Base64UrlEncode(bytes);
}
