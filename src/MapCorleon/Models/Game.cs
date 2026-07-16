namespace MapCorleon.Models;

public sealed class Game : IEntity
{
    public int Id { get; set; }
    public required string Name { get; set; }

    public ICollection<Map> Maps { get; set; } = [];
}
