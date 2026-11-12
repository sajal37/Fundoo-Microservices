using Asp.Versioning;
using Fundoo.Notes.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fundoo.Notes.Api;

[ApiController]
[Authorize]
[ApiVersion(2.0)]
[Route("api/v{version:apiVersion}/notes")]
public class NotesV2Controller : ControllerBase
{
    private readonly INoteRepository _notes;

    public NotesV2Controller(INoteRepository notes)
    {
        _notes = notes;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotes()
    {
        var userId = Guid.Parse(User.FindFirst("sub")!.Value);

        var notes = await _notes.GetNotesAsync(userId, "active");

        return Ok(notes.Select(note => new
        {
            noteId = note.Id,
            title = note.Title,
            content = note.Content,
            pinned = note.IsPinned
        }));
    }
}