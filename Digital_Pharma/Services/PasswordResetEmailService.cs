using System.Net;
using MailKit.Security;
using MimeKit;

namespace Digital_Pharma.Services;

public sealed class PasswordResetEmailService(IConfiguration configuration, ILogger<PasswordResetEmailService> logger)
{
    public async Task<bool> SendAsync(string recipient, string resetUrl, CancellationToken cancellationToken)
    {
        var subject = "استعادة كلمة مرور Digital Pharma";
        var html = $"""
            <div dir="rtl" style="font-family:Arial,sans-serif;line-height:1.8">
              <h2>استعادة كلمة المرور</h2><p>وصلنا طلب لتعيين كلمة مرور جديدة لحسابك.</p>
              <p><a href="{WebUtility.HtmlEncode(resetUrl)}">اضغط هنا لتعيين كلمة مرور جديدة</a></p>
              <p>ينتهي الرابط خلال 30 دقيقة ويُستخدم مرة واحدة فقط. إذا لم تطلب ذلك، فتجاهل الرسالة.</p>
            </div>
            """;
        return await SendMessageAsync(recipient,subject,html,cancellationToken);
    }

    public async Task<bool> SendConfirmationAsync(string recipient,string confirmationUrl,CancellationToken cancellationToken)
    {
        var subject="تأكيد بريد Digital Pharma";
        var html=$"""
            <div dir="rtl" style="font-family:Arial,sans-serif;line-height:1.8">
              <h2>تأكيد البريد الإلكتروني</h2><p>أكمل إنشاء حسابك بتأكيد هذا البريد.</p>
              <p><a href="{WebUtility.HtmlEncode(confirmationUrl)}">تأكيد البريد والدخول</a></p>
              <p>ينتهي الرابط خلال 24 ساعة ويُستخدم مرة واحدة فقط. إذا لم تنشئ الحساب، فتجاهل الرسالة.</p>
            </div>
            """;
        return await SendMessageAsync(recipient,subject,html,cancellationToken);
    }

    private async Task<bool> SendMessageAsync(string recipient,string subject,string html,CancellationToken cancellationToken)
    {
        var host = configuration["Smtp:Host"];
        var from = configuration["Smtp:From"];
        var user = configuration["Smtp:User"];
        var password = configuration["Smtp:Password"];
        var pickupDirectory = configuration["Smtp:PickupDirectory"];
        var pickupEnabled = !string.IsNullOrWhiteSpace(pickupDirectory);
        if (string.IsNullOrWhiteSpace(from) || (!pickupEnabled &&
            (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(password))))
        {
            logger.LogWarning("Password reset email was not sent because SMTP is not configured.");
            return false;
        }

        try
        {
            if (pickupEnabled)
            {
                var directory = Path.GetFullPath(pickupDirectory!);
                Directory.CreateDirectory(directory);
                using var pickup = new System.Net.Mail.SmtpClient
                {
                    DeliveryMethod = System.Net.Mail.SmtpDeliveryMethod.SpecifiedPickupDirectory,
                    PickupDirectoryLocation = directory
                };
                using var testMessage = new System.Net.Mail.MailMessage
                {
                    From = new System.Net.Mail.MailAddress(from!, configuration["Smtp:FromName"] ?? "Digital Pharma"),
                    Subject = subject, IsBodyHtml = true, Body = html
                };
                testMessage.To.Add(recipient);
                await pickup.SendMailAsync(testMessage, cancellationToken);
                return true;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(configuration["Smtp:FromName"] ?? "Digital Pharma", from));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = html }.ToMessageBody();

            using var client = new MailKit.Net.Smtp.SmtpClient { Timeout = 15000 };
            var port = configuration.GetValue("Smtp:Port", 587);
            var secureSocketOptions = configuration.GetValue("Smtp:EnableSsl", true)
                ? port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls
                : SecureSocketOptions.None;
            await client.ConnectAsync(host!, port, secureSocketOptions, cancellationToken);
            var login = new SaslMechanismLogin(user!, password!);
            await client.AuthenticateAsync(login, cancellationToken);
            await client.SendAsync(MimeKit.FormatOptions.Default, new MailKit.Net.Smtp.SmtpSendRequest(message), cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is MailKit.CommandException or MailKit.ProtocolException or
            MailKit.Security.AuthenticationException or System.Security.Authentication.AuthenticationException or
            IOException or InvalidOperationException)
        {
            logger.LogError(exception, "Password reset email delivery failed.");
            return false;
        }
    }
}
