namespace AtlasLiterario.Application.Abstractions;

public interface IEmailSender
{
    /// <summary>Sends the password reset message. The implementation builds the frontend link.</summary>
    Task SendPasswordResetAsync(string toEmail, string displayName, string resetToken, CancellationToken cancellationToken);
}
