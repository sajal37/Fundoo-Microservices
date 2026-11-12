using Fundoo.Email.Application;
using Fundoo.Email.Domain;
using Microsoft.AspNetCore.Mvc;

namespace Fundoo.Email.Api;

[ApiController]
[Route("api/email")]
public class EmailController : ControllerBase
{
    private readonly IEmailSender _emailSender;

    public EmailController(IEmailSender emailSender)
    {
        _emailSender = emailSender;
    }

    [HttpPost("send")]
    public async Task<IActionResult> Send(EmailMessage message)
    {
        try
        {
            await _emailSender.SendEmailAsync(message);
            return Ok(new { message = "Email sent successfully." });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { message = ex.Message });
        }
    }
}
