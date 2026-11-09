using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Kaloree.Api.Api;

namespace Kaloree.Api.Infrastructure;

// Never log messages: confirmation links and reset codes are credentials.
public sealed class IdentityEmail(IConfiguration config) : IEmailSender<IdentityUser>
{
    public bool Configured => !string.IsNullOrWhiteSpace(config["Email:Host"]) &&
        !string.IsNullOrWhiteSpace(config["Email:From"]) &&
        Uri.TryCreate(config["Auth:PublicUrl"], UriKind.Absolute, out var url) && url.Scheme == "https" &&
        !string.IsNullOrWhiteSpace(url.Host) && url.AbsolutePath == "/" &&
        url.Query.Length == 0 && url.Fragment.Length == 0 && url.UserInfo.Length == 0;

    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink)
    {
        // Identity builds links from the request host. Replace it with the configured public origin.
        var incoming = new Uri(WebUtility.HtmlDecode(confirmationLink));
        var origin = new Uri(config["Auth:PublicUrl"] ?? throw new InvalidOperationException("Auth:PublicUrl required"));
        var link = new Uri(origin, incoming.PathAndQuery).AbsoluteUri;
        return Send(email, "Confirm your Kaloree email", $"Confirm your email by opening this link:\n{link}");
    }

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
        throw new NotSupportedException("Use the password reset code flow");

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
        Send(email, "Reset your Kaloree password", $"Enter this reset code in Kaloree:\n{WebUtility.HtmlDecode(resetCode)}\n\nIf you did not request this, ignore this email.");

    private async Task Send(string email, string subject, string body)
    {
        if (!Configured) throw new InvalidOperationException("Email delivery is not configured");
        using var message = new MailMessage(config["Email:From"]!, email, subject, body);
        using var smtp = new SmtpClient(config["Email:Host"]!, config.GetValue("Email:Port", 587))
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(config["Email:Username"], config["Email:Password"]),
            Timeout = 15000
        };
        try { await smtp.SendMailAsync(message).WaitAsync(TimeSpan.FromSeconds(20)); }
        catch (Exception ex) when (ex is SmtpException or TimeoutException)
        { throw new ApiException(503, "email_delivery_unavailable"); }
    }
}
