namespace Menage.Core.Entregas;

/// <summary>O que entra no pacote: um arquivo (aceita * e ?) ou uma pasta compactada como .zip.</summary>
public sealed record ItemDeEntrega
{
    public string Projeto { get; init; } = "";

    /// <summary>Caminho relativo à pasta do projeto, ex.: <c>target\*.war</c>.</summary>
    public string Origem { get; init; } = "";

    public TipoDeItem Tipo { get; init; } = TipoDeItem.Arquivo;

    /// <summary>Nome no pacote; vazio = o nome de origem.</summary>
    public string Nome { get; init; } = "";
}

public enum TipoDeItem { Arquivo, PastaEmZip }

public sealed record PacoteDeEntrega
{
    public string Nome { get; init; } = "";

    /// <summary>Projeto que, ao terminar ok, dispara a pergunta (vazio = no fim da geração).</summary>
    public string Gatilho { get; init; } = "";

    /// <summary>Todos precisam terminar ok nesta geração.</summary>
    public List<string> Obrigatorios { get; init; } = [];

    /// <summary>Precisam terminar ok só se participaram da geração.</summary>
    public List<string> SeSelecionados { get; init; } = [];

    public List<ItemDeEntrega> Itens { get; init; } = [];
}

public sealed record ConfiguracaoDeEntregas
{
    public string Pasta { get; init; } = @"C:\wmw\entregas";

    /// <summary>Compacta a pasta da entrega em .zip e entrega o .zip.</summary>
    public bool Zip { get; init; } = true;

    /// <summary>Copia o .zip para a área de transferência (Ctrl+V no Slack). O programa nunca envia nada ao Slack.</summary>
    public bool CopiarZip { get; init; } = true;

    public List<PacoteDeEntrega> Pacotes { get; init; } = [];

    public static ConfiguracaoDeEntregas Padrao() => new()
    {
        Pacotes =
        [
            new PacoteDeEntrega
            {
                Nome = "Web",
                Gatilho = "lavendereweb", // sempre o último da web
                Obrigatorios = ["lavenderecore", "lavenderedados", "lavendereweb"],
                Itens =
                [
                    new ItemDeEntrega { Projeto = "lavenderedados", Origem = @"target\*.war" },
                    new ItemDeEntrega { Projeto = "lavendereweb", Origem = @"target\*.war" },
                ],
            },
            new PacoteDeEntrega
            {
                Nome = "PDA",
                Obrigatorios = ["lavenderepda"],
                SeSelecionados = ["framework-tc"],
                Itens =
                [
                    new ItemDeEntrega { Projeto = "lavenderepda", Origem = @"target\wmwvendasapp-*.jar" },
                    new ItemDeEntrega { Projeto = "lavenderepda", Origem = @"release\resources\app\simulador", Tipo = TipoDeItem.PastaEmZip, Nome = "simulador.zip" },
                ],
            },
        ],
    };
}
