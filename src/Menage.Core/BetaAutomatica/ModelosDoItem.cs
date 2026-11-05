using System.Text.Json.Serialization;

namespace Menage.Core.BetaAutomatica;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EstadoDoItem { Aguardando, Pronta, Problema, NaFila, Gerando, Concluida, Falhou, Ignorada, Finalizada }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SituacaoDaBranch { NaoVerificado, Local, Remoto, Ausente, SemRepo, Erro, Tag }

/// <summary>Resultado da geração de um projeto da atividade (texto curto mostrado no cartão).</summary>
public static class ResultadoGerado
{
    public const string Gerando = "gerando", Ok = "ok", Falhou = "falhou", Pulado = "pulado";
}

public sealed class ProjetoDoItem
{
    public string Nome { get; set; } = "";

    /// <summary>Citado pelo nome no comentário (curinga = falso).</summary>
    public bool Explicito { get; set; }

    public SituacaoDaBranch Situacao { get; set; }

    public string Detalhe { get; set; } = "";

    public string Resultado { get; set; } = "";

    /// <summary>Projeto web que o dev não alterou: entra pela tag da versão do pom da branch dele.</summary>
    public bool Complemento { get; set; }

    public string Tag { get; set; } = "";

    [JsonIgnore]
    public bool TemBranch => Situacao is SituacaoDaBranch.Local or SituacaoDaBranch.Remoto;

    [JsonIgnore]
    public bool Gera => TemBranch || (Complemento && Situacao == SituacaoDaBranch.Tag && Tag.Length > 0);
}

public sealed class ItemBeta
{
    public string Chave { get; set; } = "";

    public string Titulo { get; set; } = "";

    public string Tipo { get; set; } = "";

    public string Branch { get; set; } = "";

    public EstadoDoItem Estado { get; set; } = EstadoDoItem.Aguardando;

    public string Motivo { get; set; } = "";

    public string Erro { get; set; } = "";

    public string PastaLogs { get; set; } = "";

    public string DetectadaEm { get; set; } = "";

    /// <summary>Data/hora (yyyy-MM-dd HH:mm) da passagem para o status monitorado: a decisão vale pela passagem, não só pela chave.</summary>
    public string TransicaoEm { get; set; } = "";

    public string GeradaEm { get; set; } = "";

    public EstadoDoItem EstadoAntes { get; set; } = EstadoDoItem.Aguardando;

    public string IgnoradaEm { get; set; } = "";

    public string FinalizadaEm { get; set; } = "";

    public string FinalizadaMotivo { get; set; } = "";

    public List<string> TextoProjetos { get; set; } = [];

    public List<ProjetoDoItem> Projetos { get; set; } = [];

    public static string NomeDoEstado(EstadoDoItem e) => e switch
    {
        EstadoDoItem.Aguardando => "Aguardando branch",
        EstadoDoItem.Pronta => "Pronta para gerar",
        EstadoDoItem.Problema => "Precisa de atenção",
        EstadoDoItem.NaFila => "Na fila",
        EstadoDoItem.Gerando => "Gerando",
        EstadoDoItem.Concluida => "Concluída",
        EstadoDoItem.Falhou => "Falhou",
        EstadoDoItem.Ignorada => "Ignorada",
        _ => "Finalizada",
    };

    /// <summary>Estados a que "Reverter"/"Reabrir" podem voltar; qualquer outro vira Aguardando.</summary>
    public static EstadoDoItem EstadoValidoParaVoltar(EstadoDoItem antes) =>
        antes is EstadoDoItem.Pronta or EstadoDoItem.Problema or EstadoDoItem.Falhou or EstadoDoItem.Concluida or EstadoDoItem.Aguardando
            ? antes : EstadoDoItem.Aguardando;
}
