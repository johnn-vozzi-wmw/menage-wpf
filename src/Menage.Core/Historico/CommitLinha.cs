namespace Menage.Core.Historico;

/// <param name="Origem">Branches onde o commit foi encontrado, em texto curto.</param>
public sealed record CommitResumido(string Hash, string Curto, string Data, string Autor, string Assunto, string Origem = "");

/// <summary>Uma linha do histórico. <see cref="Texto"/> e <see cref="AutorTexto"/> são calculados uma vez, para a busca.</summary>
public sealed record CommitLinha
{
    public CommitLinha(string hash, string autor, string email, string assunto, string refs, DateTime data, bool merge = false)
    {
        Hash = hash;
        Curto = hash[..Math.Min(9, hash.Length)];
        Autor = autor;
        Email = email;
        Assunto = assunto;
        Refs = refs;
        Data = data;
        Merge = merge;
        AutorTexto = $"{autor} {email}".ToLowerInvariant();
        Texto = $"{hash} {assunto} {autor} {email} {refs}".ToLowerInvariant();
    }

    public string Hash { get; }

    public string Curto { get; }

    public string Autor { get; }

    public string Email { get; }

    public string Assunto { get; }

    public string Refs { get; }

    public DateTime Data { get; }

    public bool Merge { get; }

    /// <summary>Hash, assunto, autor, e-mail e refs em minúsculas.</summary>
    public string Texto { get; }

    /// <summary>Autor e e-mail em minúsculas.</summary>
    public string AutorTexto { get; }
}
