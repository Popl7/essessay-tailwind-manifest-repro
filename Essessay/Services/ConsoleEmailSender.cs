using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity.UI.Services;

namespace Essessay.Services;

/// <summary>
/// A stand-in mailer: it writes the message to the log instead of sending it.
///
/// Identity's confirmation and password-reset flows are useless without *some*
/// <see cref="IEmailSender"/>, and the default when none is registered is a no-op that
/// silently drops everything — so the link needed to finish registering simply never
/// exists. This at least puts it where you can click it.
///
/// It is not a mailer. Anything running this in production is not sending email.
/// </summary>
public sealed partial class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task SendEmailAsync(string email, string subject, string htmlMessage)
    {
        // The body is HTML with the link buried in an anchor. Pulling the href out and
        // logging it on its own line is the difference between a usable dev loop and
        // hand-editing an href out of an escaped blob in a terminal.
        var link = HrefPattern().Match(htmlMessage);

        logger.LogInformation(
            "Email not sent (no mailer configured)\n  to:      {Recipient}\n  subject: {Subject}\n  link:    {Link}",
            email, subject, link.Success ? System.Net.WebUtility.HtmlDecode(link.Groups[1].Value) : "(none in body)");

        return Task.CompletedTask;
    }

    [GeneratedRegex("""href=['"]([^'"]+)['"]""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();
}
