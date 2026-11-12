using Fundoo.Notes.Domain;

namespace Fundoo.Notes.Application;

public interface INoteRepository
{
    Task<List<Note>> GetNotesAsync(Guid userId, string category);
    Task<Note?> GetNoteAsync(Guid userId, Guid id);
    Task AddNoteAsync(Note note);
    Task<bool> UpdateNoteAsync(Note note);
    Task<bool> DeleteNoteAsync(Guid userId, Guid id);
    Task<bool> SetNoteStateAsync(Guid userId, Guid id, string action);
}

public interface ILabelRepository
{
    Task<List<Label>> GetLabelsAsync(Guid userId);
    Task<Label?> GetLabelAsync(Guid userId, Guid id);
    Task AddLabelAsync(Label label);
    Task<bool> RenameLabelAsync(Guid userId, Guid id, string name);
    Task<bool> DeleteLabelAsync(Guid userId, Guid id);
    Task<bool> AttachLabelAsync(Guid userId, Guid noteId, Guid labelId);
    Task<bool> RemoveLabelAsync(Guid userId, Guid noteId, Guid labelId);
}
