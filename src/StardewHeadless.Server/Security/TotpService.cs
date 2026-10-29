using System;
using System.Security.Cryptography;

namespace StardewHeadless.Server.Security;

/// <summary>
/// Layanan TOTP (Time-Based One-Time Password) berbasis RFC 6238 HMAC-SHA256
/// Menghasilkan dan memverifikasi token 6-digit yang berubah setiap 30 detik.
/// </summary>
public static class TotpService
{
    public const int TimeStepSeconds = 30;

    /// <summary>
    /// Menghasilkan 20-byte random secret key yang di-encode sebagai string Hexadesimal.
    /// </summary>
    public static string GenerateSecret()
    {
        byte[] buffer = new byte[20];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(buffer);
        return Convert.ToHexString(buffer);
    }

    /// <summary>
    /// Menghasilkan kode OTP 6-digit untuk secret key pada jendela waktu saat ini.
    /// </summary>
    public static string GenerateCode(string secretHex, DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        long timeStep = (long)(now - DateTime.UnixEpoch).TotalSeconds / TimeStepSeconds;
        return ComputeCode(secretHex, timeStep);
    }

    /// <summary>
    /// Menghitung sisa detik sebelum kode OTP saat ini berubah ke kode baru (1 s/d 30 detik).
    /// </summary>
    public static int GetRemainingSeconds(DateTime? utcNow = null)
    {
        var now = utcNow ?? DateTime.UtcNow;
        long seconds = (long)(now - DateTime.UnixEpoch).TotalSeconds;
        int remaining = TimeStepSeconds - (int)(seconds % TimeStepSeconds);
        return remaining == 0 ? TimeStepSeconds : remaining;
    }

    /// <summary>
    /// Memverifikasi kode OTP yang dimasukkan user dengan toleransi jendela waktu ±1 (drift window 90 detik).
    /// </summary>
    public static bool VerifyCode(string secretHex, string inputCode, DateTime? utcNow = null)
    {
        if (string.IsNullOrWhiteSpace(secretHex) || string.IsNullOrWhiteSpace(inputCode))
        {
            return false;
        }

        string cleanCode = inputCode.Trim().Replace(" ", "").Replace("-", "");
        if (cleanCode.Length != 6)
        {
            return false;
        }

        var now = utcNow ?? DateTime.UtcNow;
        long currentStep = (long)(now - DateTime.UnixEpoch).TotalSeconds / TimeStepSeconds;

        // Toleransi: jendela langkah sebelumnya (-1), langkah saat ini (0), dan langkah berikutnya (+1)
        for (long step = currentStep - 1; step <= currentStep + 1; step++)
        {
            if (ComputeCode(secretHex, step) == cleanCode)
            {
                return true;
            }
        }

        return false;
    }

    private static string ComputeCode(string secretHex, long timeStep)
    {
        byte[] key = Convert.FromHexString(secretHex);
        byte[] counter = BitConverter.GetBytes(timeStep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        using var hmac = new HMACSHA256(key);
        byte[] hash = hmac.ComputeHash(counter);

        int offset = hash[^1] & 0x0F;
        int binaryCode = ((hash[offset] & 0x7F) << 24)
                       | ((hash[offset + 1] & 0xFF) << 16)
                       | ((hash[offset + 2] & 0xFF) << 8)
                       | (hash[offset + 3] & 0xFF);

        int otp = binaryCode % 1000000;
        return otp.ToString("D6");
    }
}
