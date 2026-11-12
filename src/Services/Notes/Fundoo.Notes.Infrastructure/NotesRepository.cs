using Fundoo.Notes.Application;
using Fundoo.Notes.Domain;
using Npgsql;
using Microsoft.Extensions.Configuration;

namespace Fundoo.Notes.Infrastructure;

public class NotesRepository : INoteRepository, ILabelRepository
{
    private readonly string _connectionString;

    public NotesRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("NotesDb")!;
    }

    private NpgsqlConnection CreateConnection() => new NpgsqlConnection(_connectionString);

    public async Task<List<Note>> GetNotesAsync(Guid userId, string category)
    {
        var notes = new List<Note>();
        var sql = "SELECT * FROM notes WHERE user_id = @userId";
        if (category == "active") sql += " AND is_archived = false AND is_trashed = false";
        if (category == "archived") sql += " AND is_archived = true AND is_trashed = false";
        if (category == "trash") sql += " AND is_trashed = true";
        sql += " ORDER BY created_at_utc DESC";
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) notes.Add(ReadNote(reader));
        return notes;
    }

    public async Task<Note?> GetNoteAsync(Guid userId, Guid id)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT * FROM notes WHERE id=@id AND user_id=@userId", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        return await reader.ReadAsync() ? ReadNote(reader) : null;
    }

    public async Task AddNoteAsync(Note note)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var sql = "INSERT INTO notes (id,user_id,title,content,color,is_pinned,is_archived,is_trashed,created_at_utc,updated_at_utc) VALUES (@id,@userId,@title,@content,@color,@pinned,false,false,@created,@updated)";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", note.Id);
        command.Parameters.AddWithValue("userId", note.UserId);
        command.Parameters.AddWithValue("title", note.Title);
        command.Parameters.AddWithValue("content", note.Content);
        command.Parameters.AddWithValue("color", (object?)note.Color ?? DBNull.Value);
        command.Parameters.AddWithValue("pinned", note.IsPinned);
        command.Parameters.AddWithValue("created", note.CreatedAtUtc);
        command.Parameters.AddWithValue("updated", note.UpdatedAtUtc);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> UpdateNoteAsync(Note note)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        var sql = "UPDATE notes SET title=@title, content=@content, color=@color, is_pinned=@pinned, updated_at_utc=@updated WHERE id=@id AND user_id=@userId AND is_trashed=false";
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("title", note.Title);
        command.Parameters.AddWithValue("content", note.Content);
        command.Parameters.AddWithValue("color", (object?)note.Color ?? DBNull.Value);
        command.Parameters.AddWithValue("pinned", note.IsPinned);
        command.Parameters.AddWithValue("updated", DateTime.UtcNow);
        command.Parameters.AddWithValue("id", note.Id);
        command.Parameters.AddWithValue("userId", note.UserId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteNoteAsync(Guid userId, Guid id)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM notes WHERE id=@id AND user_id=@userId AND is_trashed=true", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        return await command.ExecuteNonQueryAsync() > 0;
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
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("updated", DateTime.UtcNow);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<List<Label>> GetLabelsAsync(Guid userId)
    {
        var labels = new List<Label>();
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id,user_id,name FROM labels WHERE user_id=@userId ORDER BY name", connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) labels.Add(new Label { Id = reader.GetGuid(0), UserId = reader.GetGuid(1), Name = reader.GetString(2) });
        return labels;
    }

    public async Task<Label?> GetLabelAsync(Guid userId, Guid id)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT id,user_id,name FROM labels WHERE id=@id AND user_id=@userId", connection);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new Label { Id = reader.GetGuid(0), UserId = reader.GetGuid(1), Name = reader.GetString(2) };
    }

    public async Task AddLabelAsync(Label label)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("INSERT INTO labels(id,user_id,name) VALUES(@id,@userId,@name)", connection);
        command.Parameters.AddWithValue("id", label.Id);
        command.Parameters.AddWithValue("userId", label.UserId);
        command.Parameters.AddWithValue("name", label.Name);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<bool> RenameLabelAsync(Guid userId, Guid id, string name)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE labels SET name=@name WHERE id=@id AND user_id=@userId", connection);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> DeleteLabelAsync(Guid userId, Guid id)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var unlink = new NpgsqlCommand("DELETE FROM note_labels nl USING notes n WHERE nl.note_id=n.id AND n.user_id=@userId AND nl.label_id=@id", connection, transaction))
        {
            unlink.Parameters.AddWithValue("userId", userId);
            unlink.Parameters.AddWithValue("id", id);
            await unlink.ExecuteNonQueryAsync();
        }
        await using var command = new NpgsqlCommand("DELETE FROM labels WHERE id=@id AND user_id=@userId", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("userId", userId);
        var result = await command.ExecuteNonQueryAsync() > 0;
        await transaction.CommitAsync();
        return result;
    }

    public async Task<bool> AttachLabelAsync(
    Guid userId,
    Guid noteId,
    Guid labelId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            "INSERT INTO note_labels(note_id,label_id) " +
            "SELECT n.id,l.id FROM notes n CROSS JOIN labels l " +
            "WHERE n.id=@noteId AND n.user_id=@userId " +
            "AND l.id=@labelId AND l.user_id=@userId " +
            "ON CONFLICT DO NOTHING",
            connection);

        command.Parameters.AddWithValue("noteId", noteId);
        command.Parameters.AddWithValue("labelId", labelId);
        command.Parameters.AddWithValue("userId", userId);

        return await command.ExecuteNonQueryAsync() > 0;
    }

    public async Task<bool> RemoveLabelAsync(Guid userId, Guid noteId, Guid labelId)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("DELETE FROM note_labels nl USING notes n WHERE nl.note_id=n.id AND n.id=@noteId AND n.user_id=@userId AND nl.label_id=@labelId", connection);
        command.Parameters.AddWithValue("noteId", noteId);
        command.Parameters.AddWithValue("labelId", labelId);
        command.Parameters.AddWithValue("userId", userId);
        return await command.ExecuteNonQueryAsync() > 0;
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
