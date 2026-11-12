using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Fundoo.Email.Domain;
using Fundoo.Email.Application;

namespace Fundoo.Email.Application;

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(EmailMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.Email) ||
            string.IsNullOrWhiteSpace(message.Subject) ||
            string.IsNullOrWhiteSpace(message.Body))
        {
            throw new ArgumentException("Email, subject, and body are required.");
        }

        var settings = _configuration.GetSection("EmailSettings");
        var senderEmail = settings["Email"];
        var password = settings["Password"];

        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Email settings are not configured.");
        }

        using var mail = new MailMessage();
        mail.From = new MailAddress(senderEmail);
        mail.To.Add(message.Email);
        mail.Subject = message.Subject;
        mail.Body = message.Body;
        mail.IsBodyHtml = true;

        using var smtp = new SmtpClient("smtp.gmail.com", 587);
        smtp.EnableSsl = true;
        smtp.Credentials = new NetworkCredential(senderEmail, password);

        try
        {
            await smtp.SendMailAsync(mail);
        }
        catch (SmtpException ex)
        {
            throw new HttpRequestException("The email service could not send the message.", ex);
        }
    }
}
