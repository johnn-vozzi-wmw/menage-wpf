namespace Menage.Core.Inicializacao;

/// <summary>
/// Argumentos de linha de comando que valem para o app inteiro (os de galeria e demonstração continuam em <c>ModoDeInicio</c>).
/// <c>--dados &lt;pasta&gt;</c> troca a pasta de dados (equivale ao <c>--pasta</c> do Git Manager antigo; vale também <c>--pasta</c>),
/// <c>--tema &lt;id|claro|escuro&gt;</c> troca o tema (fica como escolha salva), <c>--bandeja</c> abre minimizado,
/// <c>--medir-inicio &lt;arquivo&gt;</c> grava tempo e memória da abertura e fecha o app, <c>--ajuda</c> lista as opções.
/// </summary>
public sealed record ArgumentosDoApp(string? PastaDeDados, string? Tema, bool Minimizado, string? ArquivoDeMedicao, bool Ajuda)
{
    public const string Texto =
        "Menage.exe [opções]\n" +
        "  --dados <pasta>          usa outra pasta de dados (padrão: %LOCALAPPDATA%\\Menage)\n" +
        "  --projetos <pasta>       usa os repositórios de uma pasta (teste)\n" +
        "  --tema <id|claro|escuro> abre com esse tema (fica como escolha salva)\n" +
        "  --bandeja                abre minimizado\n" +
        "  --diff <repo> [arquivo]  janela de diff;  --conflito <repo>  janela de conflito\n" +
        "  --medir-inicio <arquivo> grava o tempo e a memória da abertura e fecha\n" +
        "  --ajuda                  mostra esta lista";

    public static ArgumentosDoApp Ler(IReadOnlyList<string> argumentos)
    {
        string? Valor(params string[] nomes)
        {
            for (var i = 0; i < argumentos.Count - 1; i++)
                if (nomes.Contains(argumentos[i], StringComparer.OrdinalIgnoreCase) && !argumentos[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return argumentos[i + 1];
            return null;
        }

        bool Tem(params string[] nomes) => argumentos.Any(a => nomes.Contains(a, StringComparer.OrdinalIgnoreCase));

        var dados = Valor("--dados", "--pasta");
        return new ArgumentosDoApp(
            string.IsNullOrWhiteSpace(dados) ? null : Path.GetFullPath(dados), Valor("--tema"), Tem("--bandeja"),
            Valor("--medir-inicio"), Tem("--ajuda", "--help", "-h", "/?"));
    }
}
