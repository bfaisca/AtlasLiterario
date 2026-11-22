namespace AtlasLiterario.Domain.Entities;

public sealed class BookAuthor
{
    private BookAuthor()
    {
    }

    public BookAuthor(Guid bookId, Guid authorId, short position)
    {
        if (position < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        BookId = bookId;
        AuthorId = authorId;
        Position = position;
    }

    public Guid BookId { get; private set; }

    public Guid AuthorId { get; private set; }

    /// <summary>Display order (0-based).</summary>
    public short Position { get; private set; }

    public Book? Book { get; private set; }

    public Author? Author { get; private set; }
}
