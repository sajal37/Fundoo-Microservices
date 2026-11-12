using System.Security.Claims;
using Fundoo.Notes.Application;
using Fundoo.Notes.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Asp.Versioning;

namespace Fundoo.Notes.Api;

[ApiController]
[Authorize]
[ApiVersion(1.0)]
[Route("api/v{version:apiVersion}/notes")]
public class NotesController : ControllerBase
{
    private readonly INoteRepository _notes;
    private readonly ILabelRepository _labels;

    public NotesController(INoteRepository notes, ILabelRepository labels)
    {
        _notes = notes;
        _labels = labels;
    }

    private Guid CurrentUserId() => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpGet]
    public Task<List<Note>> GetNotes() =>
        _notes.GetNotesAsync(CurrentUserId(), "active");

    [HttpGet("archived")]
    public Task<List<Note>> GetArchived() =>
        _notes.GetNotesAsync(CurrentUserId(), "archived");

    [HttpGet("trash")]
    public Task<List<Note>> GetTrash() =>
        _notes.GetNotesAsync(CurrentUserId(), "trash");

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetNote(Guid id)
    {
        var note = await _notes.GetNoteAsync(CurrentUserId(), id);
        return note == null ? NotFound() : Ok(note);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateNoteRequest request)
    {

        var note = new Note
        {
            Id = Guid.NewGuid(),
            UserId = CurrentUserId(),
            Title = request.Title,
            Content = request.Content,
            Color = request.Color,
            IsPinned = request.IsPinned,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        await _notes.AddNoteAsync(note);

        return CreatedAtAction(nameof(GetNote), new { id = note.Id }, note);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateNoteRequest request)
    {
        var note = await _notes.GetNoteAsync(CurrentUserId(), id);

        if (note == null)
        {
            return NotFound();
        }

        note.Title = request.Title;
        note.Content = request.Content;
        note.Color = request.Color;
        note.IsPinned = request.IsPinned;

        return await _notes.UpdateNoteAsync(note)
            ? NoContent()
            : NotFound();
    }

    [HttpPut("{id:guid}/archive")]
    public Task<bool> Archive(Guid id) =>
        _notes.SetNoteStateAsync(CurrentUserId(), id, "archive");

    [HttpPut("{id:guid}/unarchive")]
    public Task<bool> Unarchive(Guid id) =>
        _notes.SetNoteStateAsync(CurrentUserId(), id, "unarchive");

    [HttpPut("{id:guid}/trash")]
    public Task<bool> Trash(Guid id) =>
        _notes.SetNoteStateAsync(CurrentUserId(), id, "trash");

    [HttpPut("{id:guid}/restore")]
    public Task<bool> Restore(Guid id) =>
        _notes.SetNoteStateAsync(CurrentUserId(), id, "restore");

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id) =>
        await _notes.DeleteNoteAsync(CurrentUserId(), id)
            ? NoContent()
            : NotFound();

    [HttpPut("{noteId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> AttachLabel(Guid noteId, Guid labelId) =>
        await _labels.AttachLabelAsync(CurrentUserId(), noteId, labelId)
            ? NoContent()
            : NotFound();

    [HttpDelete("{noteId:guid}/labels/{labelId:guid}")]
    public async Task<IActionResult> RemoveLabel(Guid noteId, Guid labelId) =>
        await _labels.RemoveLabelAsync(CurrentUserId(), noteId, labelId)
            ? NoContent()
            : NotFound();
}