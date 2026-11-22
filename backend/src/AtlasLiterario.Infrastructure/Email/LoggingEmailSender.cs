using AtlasLiterario.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtlasLiterario.Infrastructure.Email;

/// <summary>
/// MVP e-mail sender: writes the reset link to the log in Development only.
/// In other environments the link is not logged (it is a credential) — a real sender must be configured.
/// </summary>
public sealed partial class LoggingEmailSender(
    IOptions<AppOptions> options,
    IHostEnvironment environment,
    ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendPasswordResetAsync(
        string toEmail, string displayName, string resetToken, CancellationToken cancellationToken)
    {
        if (environment.IsDevelopment())
        {
            var link = $"{options.Value.FrontendBaseUrl.TrimEnd('/')}/reset-password?token={Uri.EscapeDataString(resetToken)}";
            LogResetLink(logger, toEmail, link);
        }
        else
        {
            LogNotDelivered(logger);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "[DEV] Password reset link for {Email}: {Link}")]
    private static partial void LogResetLink(ILogger logger, string email, string link);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Password reset requested but no e-mail provider is configured; message not delivered.")]
    private static partial void LogNotDelivered(ILogger logger);
}
