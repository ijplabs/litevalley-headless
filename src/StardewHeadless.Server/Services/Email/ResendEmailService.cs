using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using StardewHeadless.Protocol.Logging;

namespace StardewHeadless.Server.Services.Email;

public class ResendEmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly StructuredLogger? _logger;
    private readonly string? _apiKey;
    private readonly string _fromEmail;

    public ResendEmailService(StructuredLogger? logger = null, HttpClient? httpClient = null, string? apiKeyOverride = null)
    {
        _logger = logger;
        _httpClient = httpClient ?? new HttpClient();
        _apiKey = apiKeyOverride ?? Environment.GetEnvironmentVariable("RESEND_API_KEY");
        _fromEmail = Environment.GetEnvironmentVariable("RESEND_FROM_EMAIL") ?? "LiteValley <onboarding@resend.dev>";
    }

    public async Task<(bool success, string? errorMessage)> SendVerificationEmailAsync(string toEmail, string username, string verificationCode)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger?.Warn("ResendEmail", $"[DEV MODE] RESEND_API_KEY belum diset di Environment. Kode verifikasi untuk '{username}' ({toEmail}): {verificationCode}");
            Console.WriteLine($"[ResendEmail:DEV] OTP untuk {username} ({toEmail}): {verificationCode}");
            return (true, null);
        }

        try
        {
            var payload = new
            {
                from = _fromEmail,
                to = new[] { toEmail.Trim() },
                subject = "Kode Verifikasi Email Peternakan - LiteValley",
                html = $@"
                    <div style=""font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; max-width: 540px; margin: 0 auto; background: #0f172a; color: #f8fafc; border-radius: 12px; padding: 2rem; border: 1px solid #334155;"">
                        <div style=""text-align: center; margin-bottom: 1.5rem;"">
                            <h2 style=""color: #f59e0b; margin: 0; font-size: 1.5rem;"">🌾 LiteValley Dedicated Farm</h2>
                            <p style=""color: #94a3b8; font-size: 0.9rem; margin-top: 0.25rem;"">Verifikasi Alamat Email Warga Baru</p>
                        </div>
                        <p style=""font-size: 1rem; line-height: 1.5; color: #cbd5e1;"">
                            Halo <strong>{username}</strong>,<br/>
                            Terima kasih telah mendaftar di Peternakan Komunal LiteValley. Gunakan kode 6-digit berikut untuk mengonfirmasi email Anda:
                        </p>
                        <div style=""text-align: center; margin: 2rem 0;"">
                            <span style=""display: inline-block; background: #1e293b; color: #38bdf8; font-size: 2.25rem; font-weight: bold; letter-spacing: 8px; padding: 0.75rem 1.75rem; border-radius: 8px; border: 1px solid #0284c7;"">
                                {verificationCode}
                            </span>
                        </div>
                        <p style=""font-size: 0.85rem; color: #94a3b8; text-align: center; margin-bottom: 2rem;"">
                            Kode ini berlaku selama <strong>15 menit</strong>. Jangan bagikan kode ini kepada siapa pun.
                        </p>
                        <hr style=""border: 0; border-top: 1px solid #334155; margin: 1.5rem 0;"" />
                        <p style=""font-size: 0.75rem; color: #64748b; text-align: center; margin: 0;"">
                            Jika Anda tidak merasa mendaftar di LiteValley, silakan abaikan email ini.
                        </p>
                    </div>"
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey.Trim());
            request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger?.Info("ResendEmail", $"Email verifikasi berhasil dikirim via Resend ke {toEmail} untuk user '{username}'.");
                return (true, null);
            }
            else
            {
                var errorMsg = $"Gagal mengirim email via Resend ({response.StatusCode}): {responseBody}";
                _logger?.Warn("ResendEmail", errorMsg);
                return (false, errorMsg);
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"Exception saat memanggil Resend API: {ex.Message}";
            _logger?.Error("ResendEmail", errorMsg, null, ex);
            return (false, errorMsg);
        }
    }
}
