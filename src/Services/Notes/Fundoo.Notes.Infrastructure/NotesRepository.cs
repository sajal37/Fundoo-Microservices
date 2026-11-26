using Fundoo.Notes.Application;
using Fundoo.Notes.Domain;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System.Text.Json;

namespace Fundoo.Notes.Infrastructure;

public class NotesRepository : INoteRepository, ILabelRepository
{
    private readonly string _connectionString;
    private readonly IDistributedCache _cache;

    public NotesRepository(
        IConfiguration configuration,
        IDistributedCache cache)
    {
        _connectionString =
            configuration.GetConnectionString("NotesDb")!;

        _cache = cache;
    }

    private NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public async Task<List<Note>> GetNotesAsync(Guid userId, string category)
    {
        string cacheKey = $"notes:{userId}:{category}";
        string? cachedNotes = await TryGetCacheAsync(cacheKey);

        if (cachedNotes != null)
        {
            List<Note>? parsed = JsonSerializer.Deserialize<List<Note>>(cachedNotes);
            if (parsed != null)
            {
                return parsed;
            }
        }

        List<Note> notes = new List<Note>();
        string sql = "SELECT * FROM notes WHERE user_id = @userId";

        if (category == "active")
            sql += " AND is_archived = false AND is_trashed = false";

        if (category == "archived")
            sql += " AND is_archived = true AND is_trashed = false";

        if (category == "trash")
            sql += " AND is_trashed = true";

        sql += " ORDER BY created_at_utc DESC";

        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        await using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("userId", userId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                notes.Add(ReadNote(reader));
            }
        }

        foreach (Note note in notes)
        {
            note.Labels = await LoadLabelsForNoteAsync(connection, note.Id);
        }

        await TrySetCacheAsync(cacheKey, JsonSerializer.Serialize(notes));

        return notes;
    }

    public async Task<List<Note>> GetSharedNotesAsync(Guid userId)
    {
        string cacheKey = $"notes:{userId}:shared";
        string? cachedNotes = await TryGetCacheAsync(cacheKey);

        if (cachedNotes != null)
        {
            List<Note>? parsed = JsonSerializer.Deserialize<List<Note>>(cachedNotes);
            if (parsed != null)
            {
                return parsed;
            }
        }

        List<Note> notes = new List<Note>();
        string sql =
            "SELECT n.* FROM notes n " +
            "INNER JOIN note_collaborators nc ON n.id = nc.note_id " +
            "WHERE nc.user_id = @userId AND n.is_trashed = false " +
            "ORDER BY n.created_at_utc DESC";

        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        await using (NpgsqlCommand command = new NpgsqlCommand(sql, connection))
        {
            command.Parameters.AddWithValue("userId", userId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                notes.Add(ReadNote(reader));
            }
        }

        foreach (Note note in notes)
        {
            note.Labels = await LoadLabelsForNoteAsync(connection, note.Id);
        }

        await TrySetCacheAsync(cacheKey, JsonSerializer.Serialize(notes));

        return notes;
    }

    public async Task<Note?> GetNoteAsync(Guid userId, Guid id)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        bool canAccess = await UserCanAccessNoteAsync(connection, userId, id);
        if (!canAccess)
        {
            return null;
        }

        string noteCacheKey = $"note:{id}";
        string? cachedNote = await TryGetCacheAsync(noteCacheKey);

        if (cachedNote != null)
        {
            Note? parsed = JsonSerializer.Deserialize<Note>(cachedNote);
            if (parsed != null)
            {
                return parsed;
            }
        }

        Note? note = null;
        await using (NpgsqlCommand command = new NpgsqlCommand("SELECT * FROM notes WHERE id=@id", connection))
        {
            command.Parameters.AddWithValue("id", id);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                note = ReadNote(reader);
            }
        }

        if (note == null)
        {
            return null;
        }

        note.Labels = await LoadLabelsForNoteAsync(connection, note.Id);
        await TrySetCacheAsync(noteCacheKey, JsonSerializer.Serialize(note));

        return note;
    }

    public async Task AddNoteAsync(Note note)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        string sql = "INSERT INTO notes (id,user_id,title,content,color,is_pinned,is_archived,is_trashed,created_at_utc,updated_at_utc) VALUES (@id,@userId,@title,@content,@color,@pinned,false,false,@created,@updated)";
        await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", note.Id);
        command.Parameters.AddWithValue("userId", note.UserId);
        command.Parameters.AddWithValue("title", note.Title);
        command.Parameters.AddWithValue("content", note.Content);
        command.Parameters.AddWithValue("color", (object?)note.Color ?? DBNull.Value);
        command.Parameters.AddWithValue("pinned", note.IsPinned);
        command.Parameters.AddWithValue("created", note.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", note.UpdatedAtUtc);
        await command.ExecuteNonQueryAsync();
        await ClearNotesCacheAsync(note.UserId);
    }

    public async Task<bool> UpdateNoteAsync(Note note)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        string sql = "UPDATE notes SET title=@title, content=@content, color=@color, is_pinned=@pinned, updated_at_utc=@updated WHERE id=@id AND user_id=@userId AND is_trashed=false";
        await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("title", note.Title);
        command.Parameters.AddWithValue("content", note.Content);
        command.Parameters.AddWithValue("color", (object?)note.Color ?? DBNull.Value);
        command.Parameters.AddWithValue("pinned", note.IsPinned);
        command.Parameters.AddWithValue("updated", DateTime.UtcNow);
        command.Parameters.AddWithValue("id", note.Id);
        command.Parameters.AddWithValue("userId", note.UserId);
        bool updated = await command.ExecuteNonQueryAsync() > 0;

        if (updated)
        {
            await ClearNoteAndUsersCacheAsync(connection, note.Id, note.UserId);
        }

        return updated;
    }

    public async Task<bool> DeleteNoteAsync(Guid userId, Guid id)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        List<Guid> collaboratorIds = await GetCollaboratorIdsInternalAsync(connection, id);

        await using NpgsqlCommand command = new NpgsqlCommand("DELETE FROM notes WHERE id=@id AND user_id=@userId AND is_trashed=true", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        bool deleted = await command.ExecuteNonQueryAsync() > 0;

        if (deleted)
        {
            await TryRemoveCacheAsync($"note:{id}");
            await ClearNotesCacheAsync(userId);
            foreach (Guid collaboratorId in collaboratorIds)
            {
                await TryRemoveCacheAsync($"notes:{collaboratorId}:shared");
            }
        }

        return deleted;
    }

    public async Task<bool> SetNoteStateAsync(Guid userId, Guid id, string action)
    {
        string sql = action switch
        {
            "archive" => "UPDATE notes SET is_archived=true, updated_at_utc=@updated WHERE id=@id AND user_id=@userId AND is_trashed=false",
            "unarchive" => "UPDATE notes SET is_archived=false, updated_at_utc=@updated WHERE id=@id AND user_id=@userId AND is_trashed=false",
            "trash" => "UPDATE notes SET is_trashed=true, is_archived=false, updated_at_utc=@updated WHERE id=@id AND user_id=@userId",
            "restore" => "UPDATE notes SET is_trashed=false, updated_at_utc=@updated WHERE id=@id AND user_id=@userId AND is_trashed=true",
            _ => ""
        };
        if (sql == "") return false;

        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("updated", DateTime.UtcNow);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        bool updated = await command.ExecuteNonQueryAsync() > 0;

        if (updated)
        {
            await ClearNoteAndUsersCacheAsync(connection, id, userId);
        }

        return updated;
    }

    public async Task<bool> AddCollaboratorAsync(Guid ownerUserId, Guid noteId, Guid collaboratorUserId)
    {
        if (ownerUserId == collaboratorUserId)
        {
            return false;
        }

        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        bool isOwner = await IsActiveOwnerNoteAsync(connection, ownerUserId, noteId);
        if (!isOwner)
        {
            return false;
        }

        string sql =
            "INSERT INTO note_collaborators (note_id, user_id, created_at_utc) " +
            "VALUES (@noteId, @collaboratorUserId, @createdAtUtc) " +
            "ON CONFLICT DO NOTHING";

        await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("noteId", noteId);
        command.Parameters.AddWithValue("collaboratorUserId", collaboratorUserId);
        command.Parameters.AddWithValue("createdAtUtc", DateTime.UtcNow);
        await command.ExecuteNonQueryAsync();

        await ClearNoteAndUsersCacheAsync(connection, noteId, ownerUserId, collaboratorUserId);

        return true;
    }

    public async Task<bool> RemoveCollaboratorAsync(Guid ownerUserId, Guid noteId, Guid collaboratorUserId)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        string sql =
            "DELETE FROM note_collaborators nc " +
            "USING notes n " +
            "WHERE nc.note_id = n.id AND n.id = @noteId AND n.user_id = @ownerUserId AND nc.user_id = @collaboratorUserId";

        await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("noteId", noteId);
        command.Parameters.AddWithValue("ownerUserId", ownerUserId);
        command.Parameters.AddWithValue("collaboratorUserId", collaboratorUserId);
        bool removed = await command.ExecuteNonQueryAsync() > 0;

        if (removed)
        {
            await ClearNoteAndUsersCacheAsync(connection, noteId, ownerUserId, collaboratorUserId);
        }

        return removed;
    }

    public async Task<List<Guid>> GetCollaboratorsAsync(Guid userId, Guid noteId)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        bool canAccess = await UserCanAccessNoteAsync(connection, userId, noteId);
        if (!canAccess)
        {
            return new List<Guid>();
        }

        return await GetCollaboratorIdsInternalAsync(connection, noteId);
    }

    public async Task<List<Label>> GetLabelsAsync(Guid userId)
    {
        List<Label> labels = new List<Label>();
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        await using NpgsqlCommand command = new NpgsqlCommand("SELECT id,user_id,name FROM labels WHERE user_id=@userId ORDER BY name", connection);
        command.Parameters.AddWithValue("userId", userId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) labels.Add(new Label { Id = reader.GetGuid(0), UserId = reader.GetGuid(1), Name = reader.GetString(2) });
        return labels;
    }

    public async Task<Label?> GetLabelAsync(Guid userId, Guid id)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        await using NpgsqlCommand command = new NpgsqlCommand("SELECT id,user_id,name FROM labels WHERE id=@id AND user_id=@userId", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Label { Id = reader.GetGuid(0), UserId = reader.GetGuid(1), Name = reader.GetString(2) };
    }

    public async Task AddLabelAsync(Label label)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        await using NpgsqlCommand command = new NpgsqlCommand("INSERT INTO labels(id,user_id,name) VALUES(@id,@userId,@name)", connection);
        command.Parameters.AddWithValue("id", label.Id);
        command.Parameters.AddWithValue("userId", label.UserId);
        command.Parameters.AddWithValue("name", label.Name);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> RenameLabelAsync(Guid userId, Guid id, string name)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        await using NpgsqlCommand command = new NpgsqlCommand("UPDATE labels SET name=@name WHERE id=@id AND user_id=@userId", connection);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        bool updated = await command.ExecuteNonQueryAsync() > 0;

        if (updated)
        {
            List<Guid> noteIds = await GetNoteIdsForLabelAsync(connection, userId, id);
            foreach (Guid noteId in noteIds)
            {
                await ClearNoteAndUsersCacheAsync(connection, noteId, userId);
            }
        }

        return updated;
    }

    public async Task<bool> DeleteLabelAsync(Guid userId, Guid id)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        List<Guid> noteIds = await GetNoteIdsForLabelAsync(connection, userId, id);

        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await using (NpgsqlCommand unlink = new NpgsqlCommand("DELETE FROM note_labels nl USING notes n WHERE nl.note_id=n.id AND n.user_id=@userId AND nl.label_id=@id", connection, transaction))
        {
            unlink.Parameters.AddWithValue("userId", userId);
            unlink.Parameters.AddWithValue("id", id);
            await unlink.ExecuteNonQueryAsync();
        }
        await using NpgsqlCommand command = new NpgsqlCommand("DELETE FROM labels WHERE id=@id AND user_id=@userId", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        bool result = await command.ExecuteNonQueryAsync() > 0;
        await transaction.CommitAsync();

        if (result)
        {
            foreach (Guid noteId in noteIds)
            {
                await ClearNoteAndUsersCacheAsync(connection, noteId, userId);
            }
        }

        return result;
    }

    public async Task<bool> AttachLabelAsync(
        Guid userId,
        Guid noteId,
        Guid labelId)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();

        await using NpgsqlCommand command = new NpgsqlCommand(
            "INSERT INTO note_labels(note_id,label_id) " +
            "SELECT n.id,l.id FROM notes n CROSS JOIN labels l " +
            "WHERE n.id=@noteId AND n.user_id=@userId " +
            "AND l.id=@labelId AND l.user_id=@userId " +
            "ON CONFLICT DO NOTHING",
            connection);

        command.Parameters.AddWithValue("noteId", noteId);
        command.Parameters.AddWithValue("labelId", labelId);
        command.Parameters.AddWithValue("userId", userId);

        bool attached = await command.ExecuteNonQueryAsync() > 0;

        if (attached)
        {
            await ClearNoteAndUsersCacheAsync(connection, noteId, userId);
        }

        return attached;
    }

    public async Task<bool> RemoveLabelAsync(Guid userId, Guid noteId, Guid labelId)
    {
        await using NpgsqlConnection connection = CreateConnection();
        await connection.OpenAsync();
        await using NpgsqlCommand command = new NpgsqlCommand("DELETE FROM note_labels nl USING notes n WHERE nl.note_id=n.id AND n.id=@noteId AND n.user_id=@userId AND nl.label_id=@labelId", connection);
        command.Parameters.AddWithValue("noteId", noteId);
        command.Parameters.AddWithValue("labelId", labelId);
        command.Parameters.AddWithValue("userId", userId);
        bool removed = await command.ExecuteNonQueryAsync() > 0;

        if (removed)
        {
            await ClearNoteAndUsersCacheAsync(connection, noteId, userId);
        }

        return removed;
    }

    private async Task<bool> UserCanAccessNoteAsync(NpgsqlConnection connection, Guid userId, Guid noteId)
    {
        Guid ownerId = Guid.Empty;
        bool isTrashed = false;
        bool found = false;

        await using (NpgsqlCommand command = new NpgsqlCommand("SELECT user_id, is_trashed FROM notes WHERE id=@noteId", connection))
        {
            command.Parameters.AddWithValue("noteId", noteId);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                found = true;
                ownerId = reader.GetGuid(0);
                isTrashed = reader.GetBoolean(1);
            }
        }

        if (!found)
        {
            return false;
        }

        if (ownerId == userId)
        {
            return true;
        }

        if (isTrashed)
        {
            return false;
        }

        await using NpgsqlCommand collabCommand = new NpgsqlCommand(
            "SELECT 1 FROM note_collaborators WHERE note_id=@noteId AND user_id=@userId",
            connection);
        collabCommand.Parameters.AddWithValue("noteId", noteId);
        collabCommand.Parameters.AddWithValue("userId", userId);
        object? result = await collabCommand.ExecuteScalarAsync();

        return result != null;
    }

    private async Task<bool> IsActiveOwnerNoteAsync(NpgsqlConnection connection, Guid ownerUserId, Guid noteId)
    {
        await using NpgsqlCommand command = new NpgsqlCommand(
            "SELECT 1 FROM notes WHERE id=@noteId AND user_id=@ownerUserId AND is_trashed=false",
            connection);
        command.Parameters.AddWithValue("noteId", noteId);
        command.Parameters.AddWithValue("ownerUserId", ownerUserId);
        object? result = await command.ExecuteScalarAsync();
        return result != null;
    }

    private async Task<List<Label>> LoadLabelsForNoteAsync(NpgsqlConnection connection, Guid noteId)
    {
        List<Label> labels = new List<Label>();
        string sql =
            "SELECT l.id, l.user_id, l.name " +
            "FROM labels l " +
            "INNER JOIN note_labels nl ON l.id = nl.label_id " +
            "WHERE nl.note_id = @noteId " +
            "ORDER BY l.name";

        await using NpgsqlCommand command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("noteId", noteId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            labels.Add(new Label
            {
                Id = reader.GetGuid(0),
                UserId = reader.GetGuid(1),
                Name = reader.GetString(2)
            });
        }

        return labels;
    }

    private async Task<List<Guid>> GetCollaboratorIdsInternalAsync(NpgsqlConnection connection, Guid noteId)
    {
        List<Guid> ids = new List<Guid>();
        await using NpgsqlCommand command = new NpgsqlCommand(
            "SELECT user_id FROM note_collaborators WHERE note_id=@noteId",
            connection);
        command.Parameters.AddWithValue("noteId", noteId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private async Task<List<Guid>> GetNoteIdsForLabelAsync(NpgsqlConnection connection, Guid userId, Guid labelId)
    {
        List<Guid> noteIds = new List<Guid>();
        await using NpgsqlCommand command = new NpgsqlCommand(
            "SELECT nl.note_id FROM note_labels nl INNER JOIN notes n ON nl.note_id = n.id WHERE nl.label_id=@labelId AND n.user_id=@userId",
            connection);
        command.Parameters.AddWithValue("labelId", labelId);
        command.Parameters.AddWithValue("userId", userId);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            noteIds.Add(reader.GetGuid(0));
        }

        return noteIds;
    }

    private async Task ClearNoteAndUsersCacheAsync(
        NpgsqlConnection connection,
        Guid noteId,
        Guid ownerUserId,
        Guid? extraCollaboratorId = null)
    {
        await TryRemoveCacheAsync($"note:{noteId}");
        await ClearNotesCacheAsync(ownerUserId);

        List<Guid> collaboratorIds = await GetCollaboratorIdsInternalAsync(connection, noteId);
        foreach (Guid collaboratorId in collaboratorIds)
        {
            await TryRemoveCacheAsync($"notes:{collaboratorId}:shared");
        }

        if (extraCollaboratorId.HasValue)
        {
            await TryRemoveCacheAsync($"notes:{extraCollaboratorId.Value}:shared");
        }
    }

    private async Task ClearNotesCacheAsync(Guid userId)
    {
        await TryRemoveCacheAsync($"notes:{userId}:active");
        await TryRemoveCacheAsync($"notes:{userId}:archived");
        await TryRemoveCacheAsync($"notes:{userId}:trash");
    }

    private async Task<string?> TryGetCacheAsync(string key)
    {
        try
        {
            return await _cache.GetStringAsync(key);
        }
        catch
        {
            return null;
        }
    }

    private async Task TrySetCacheAsync(string key, string value)
    {
        try
        {
            await _cache.SetStringAsync(
                key,
                value,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
                });
        }
        catch
        {
        }
    }

    private async Task TryRemoveCacheAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key);
        }
        catch
        {
        }
    }

    private static Note ReadNote(NpgsqlDataReader reader) => new()
    {
        Id = reader.GetGuid(reader.GetOrdinal("id")),
        UserId = reader.GetGuid(reader.GetOrdinal("user_id")),
        Title = reader.GetString(reader.GetOrdinal("title")),
        Content = reader.GetString(reader.GetOrdinal("content")),
        Color = reader.IsDBNull(reader.GetOrdinal("color")) ? null : reader.GetString(reader.GetOrdinal("color")),
        IsPinned = reader.GetBoolean(reader.GetOrdinal("is_pinned")),
        IsArchived = reader.GetBoolean(reader.GetOrdinal("is_archived")),
        IsTrashed = reader.GetBoolean(reader.GetOrdinal("is_trashed")),
        CreatedAtUtc = reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
        UpdatedAtUtc = reader.GetDateTime(reader.GetOrdinal("updated_at_utc"))
    };
}
