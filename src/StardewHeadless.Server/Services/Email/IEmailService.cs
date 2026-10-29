using System.Threading.Tasks;

namespace StardewHeadless.Server.Services.Email;

public interface IEmailService
{
    Task<(bool success, string? errorMessage)> SendVerificationEmailAsync(string toEmail, string username, string verificationCode);
}
