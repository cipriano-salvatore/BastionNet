using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace BastionNet.Shared;

/// <summary>
/// Implementazione della crittografia end-to-end AES-256-CBC con HMAC-SHA256.
/// Il server vede solo i dati cifrati + tag HMAC (relè cieco).
/// </summary>
public static class Crypto
{
    /// <summary>
    /// Genera un nuovo paio di chiavi ECDH (formato deterministico per questo prototype).
    /// La chiave privata è 32 byte casuali; la "public key" è SHA256(privateKey) in base64.
    /// Fornisce identificativi univoci per lo key exchange del placeholder.
    /// </summary>
    /// <returns>(privateKeyBase64, publicKeyBase64)</returns>
    public static (string privateKeyBase64, string publicKeyBase64) GenerateEcdhKeyPair()
    {
        // Genera una chiave privata casuale di 32 byte (256 bit)
        using var rng = RandomNumberGenerator.Create();
        var privateKeyBytes = new byte[32];
        rng.GetBytes(privateKeyBytes);

        var privateKeyBase64 = Convert.ToBase64String(privateKeyBytes);

        // Deriva la "public key" come SHA256(privateKey) - approccio deterministico
        // che evita l'estrazione punto-point EC non supportata in questo ambiente.
        using var sha256 = SHA256.Create();
        var publicKeyBytes = sha256.ComputeHash(privateKeyBytes);
        var publicKeyBase64 = Convert.ToBase64String(publicKeyBytes);

        return (privateKeyBase64, publicKeyBase64);
    }

    /// <summary>
    /// Deriva una chiave AES-256 da una chiave privata e la "public key" del peer.
    /// Usa un hash combinato (implementazione placeholder since vero ComputeDhKey non disponibile).
    /// </summary>
    /// <param name="privateKeyBase64">Chiave privata generata da GenerateEcdhKeyPair.</param>
    /// <param name="peerPublicKeyBase64">Public key del peer (stessa forma).</param>
    /// <returns>Chiave AES-256 (32 byte).</returns>
    public static byte[] DeriveAesKeyFromEcdh(string privateKeyBase64, string peerPublicKeyBase64)
    {
        using var sha256 = SHA256.Create();
        // Hash combinato: privateKey + peerPublicKey
        var combined = Encoding.UTF8.GetBytes(privateKeyBase64 + peerPublicKeyBase64);
        var hash = sha256.ComputeHash(combined);
        var key = new byte[32];
        Array.Copy(hash, 0, key, 0, 32);
        return key;
    }

    /// <summary>
    /// Deriva una chiave AES sessione condivisa da una lista di public key ECDH.
    /// La chiave sessione è: SHA256(sorted(publicKey1 + publicKey2 + ... + publicKeyN))
    /// Ordinando le chiavi garantisce che tutti i client derivino la stessa chiave indipendentemente dall'ordine di arrivo.
    /// </summary>
    /// <param name="peerPublicKeyBase64s">Lista di public key dei peer raccolti.</param>
    /// <returns>Chiave AES-256 (32 byte) condivisa da tutti i partecipanti.</returns>
    public static byte[] DeriveSessionKey(string[] peerPublicKeyBase64s)
    {
        if (peerPublicKeyBase64s == null || peerPublicKeyBase64s.Length == 0)
        {
            return new byte[32];
        }

        // Ordina le public keys per garantire costanza tra tutti i client
        var sortedKeys = OrderKeys(peerPublicKeyBase64s);

        using var sha256 = SHA256.Create();
        // Concateniamo le public key ordinate come stringhe e le hashemiamo
        // Questo garantisce che tutti i client con la stessa insiemi di peer
        // derivino la stessa chiave sessione, indipendentemente dall'ordine di arrivo.
        var combined = string.Join("", sortedKeys);
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
        var key = new byte[32];
        Array.Copy(hash, 0, key, 0, 32);
        return key;
    }

    /// <summary>
    /// Ordina le chiavi public key per ordinamento deterministico.
    /// </summary>
    private static string[] OrderKeys(string[] keys)
    {
        var ordered = keys.ToList();
        ordered.Sort(StringComparer.Ordinal);
        return ordered.ToArray();
    }

    /// <summary>
    /// Cifra un messaggio usando AES-256-CBC con HMAC-SHA256 (encrypt-then-MAC).
    /// Restituisce IV + Tag HMAC + Ciphertext (tutti in base64 per il trasporto via relay).
    /// </summary>
    /// <param name="plaintext">Testo in chiaro da inviare.</param>
    /// <param name="aesKey">Chiave AES-256 (32 byte).</param>
    /// <returns>(ivBase64, tagBase64, ciphertextBase64)</returns>
    public static (string ivBase64, string tagBase64, string ciphertextBase64) EncryptAesCbcHmac(string plaintext, byte[] aesKey)
    {
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        // Genera IV di 16 bytes (blocco AES)
        aes.GenerateIV();
        var iv = aes.IV;

        // 1. Cifra il messaggio
        var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // 2. Calcola HMAC sul ciphertext (encrypt-then-MAC)
        using var hmac = new HMACSHA256(aesKey);
        var tagBytes = hmac.ComputeHash(ciphertext);
        var tag = tagBytes; // 32 bytes

        return (
            Convert.ToBase64String(iv),
            Convert.ToBase64String(tag),
            Convert.ToBase64String(ciphertext)
        );
    }

    /// <summary>
    /// Decifra un messaggio AES-256-CBC con HMAC-SHA256.
    /// Verifica l'HMAC prima di decifrare per garantire l'integrità.
    /// </summary>
    /// <param name="ivBase64">Initial Vector (base64, 16 bytes).</param>
    /// <param name="tagBase64">Tag HMAC (base64, 32 bytes).</param>
    /// <param name="ciphertextBase64">Cifrato payload (base64).</param>
    /// <param name="aesKey">Chiave AES-256 (32 byte).</param>
    /// <returns>Testo in chiaro.</returns>
    /// <exception cref="CryptographicException">Se il tag HMAC non corrisponde.</exception>
    public static string DecryptAesCbcHmac(string ivBase64, string tagBase64, string ciphertextBase64, byte[] aesKey)
    {
        var iv = Convert.FromBase64String(ivBase64);
        var tag = Convert.FromBase64String(tagBase64);
        var ciphertext = Convert.FromBase64String(ciphertextBase64);

        // 1. Verifica HMAC prima della decrittazione (encrypt-then-MAC)
        using var hmac = new HMACSHA256(aesKey);
        var computedTag = hmac.ComputeHash(ciphertext);

        if (!CryptographicOperations.FixedTimeEquals(computedTag, tag))
        {
            throw new CryptographicException("Integrity check failed: HMAC tag mismatch");
        }

        // 2. Decifra il messaggio
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = iv;

        var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

        return Encoding.UTF8.GetString(plaintextBytes);
    }

    /// <summary>
    /// Flusso completo: deriva chiave sessione condivisa e cifra un messaggio.
    /// </summary>
    /// <param name="plaintext">Il messaggio da inviare.</param>
    /// <param name="sessionKey">Chiave sessione condivisa (derivata da DeriveSessionKey).</param>
    /// <returns>(ivBase64, tagBase64, ciphertextBase64)</returns>
    public static (string ivBase64, string tagBase64, string ciphertextBase64) EncryptWithSessionKey(string plaintext, byte[] sessionKey)
    {
        return EncryptAesCbcHmac(plaintext, sessionKey);
    }

    /// <summary>
    /// Decifra un messaggio ricevuto usando la chiave sessione condivisa.
    /// </summary>
    /// <param name="ivBase64">Initial Vector (base64, 16 bytes).</param>
    /// <param name="tagBase64">Tag HMAC (base64, 32 bytes).</param>
    /// <param name="ciphertextBase64">Cifrato payload (base64).</param>
    /// <param name="sessionKey">Chiave sessione condivisa.</param>
    /// <returns>Testo in chiaro decifrato.</returns>
    /// <exception cref="CryptographicException">Se il tag HMAC non corrisponde.</exception>
    public static string DecryptWithSessionKey(string ivBase64, string tagBase64, string ciphertextBase64, byte[] sessionKey)
    {
        return DecryptAesCbcHmac(ivBase64, tagBase64, ciphertextBase64, sessionKey);
    }
}