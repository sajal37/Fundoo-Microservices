using Fundoo.Email.Domain;

namespace Fundoo.Email.Application;

public interface IEmailSender
{
    Task SendEmailAsync(EmailMessage message);
}
