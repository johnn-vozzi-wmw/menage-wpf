namespace Menage.Core.Diff;

/// <summary>
/// Duas letras que identificam o arquivo na lista (iniciais do nome da classe).
/// Regra: nome sem extensão, dividido em palavras (CamelCase, siglas, "_", "-", ".", espaço);
/// várias palavras → 1ª letra da primeira + 1ª letra da última (<c>PedidoService</c> → PS);
/// uma palavra → as duas primeiras letras (<c>Main</c> → MA). Sem 2 letras utilizáveis → "??".
/// </summary>
public static partial class IniciaisDeArquivo
{
    public const string Fallback = "??";

    public static string De(string? caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return Fallback;
        var nome = caminho[(caminho.LastIndexOfAny(['/', '\\']) + 1)..];
        var ponto = nome.LastIndexOf('.');
        if (ponto > 0) nome = nome[..ponto];

        // sufixo de idioma (Messages_en_US, Labels_pt) não é parte do nome
        nome = Idioma().Replace(nome, "");
        var palavras = Palavras(nome);
        if (palavras.Count == 0) return Fallback;
        if (palavras.Count == 1)
            return palavras[0].Length >= 2 ? palavras[0][..2].ToUpperInvariant() : Fallback;
        return (palavras[0][..1] + palavras[^1][..1]).ToUpperInvariant();
    }

    [System.Text.RegularExpressions.GeneratedRegex("(?<=[A-Za-z])[_-][a-z]{2}([_-][A-Z]{2})?$")]
    private static partial System.Text.RegularExpressions.Regex Idioma();

    static List<string> Palavras(string nome)
    {
        var lista = new List<string>();
        var atual = new System.Text.StringBuilder();
        void Fecha()
        {
            if (atual.Length > 0) lista.Add(atual.ToString());
            atual.Clear();
        }

        for (var i = 0; i < nome.Length; i++)
        {
            var c = nome[i];
            if (!char.IsLetter(c)) { Fecha(); continue; } // separador, número ou símbolo
            if (atual.Length > 0 && char.IsUpper(c))
            {
                var anteriorMinuscula = char.IsLower(nome[i - 1]);
                var fimDeSigla = char.IsUpper(nome[i - 1]) && i + 1 < nome.Length && char.IsLower(nome[i + 1]);
                if (anteriorMinuscula || fimDeSigla) Fecha();
            }
            atual.Append(c);
        }
        Fecha();
        return lista;
    }
}
