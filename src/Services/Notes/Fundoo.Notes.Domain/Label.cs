namespace Fundoo.Notes.Domain;

public class Label
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = "";
}
