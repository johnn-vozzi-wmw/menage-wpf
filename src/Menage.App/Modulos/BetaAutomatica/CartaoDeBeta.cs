using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Um repositório da atividade no checklist: marca (✓ … ✕ –), cor e o texto pronto/aguardando/problema/gerando/pulado.</summary>
public sealed record ProjetoDoCartao(string Nome, string Detalhe, string Marca, TomDeCor Tom, string Situacao, bool Pronto);

/// <summary>Uma atividade da lista (item e detalhe). Só apresenta: as regras estão no <see cref="ServicoDeBetaAutomatica"/>.</summary>
public sealed class CartaoDeBeta
{
    public CartaoDeBeta(ItemBeta item, Action<string, string> acao)
    {
        Chave = item.Chave;
        Titulo = item.Titulo;
        Estado = item.Estado;
        EstadoTexto = TextoDoEstado(item.Estado);
        Tom = TomDoEstado(item.Estado);
        Branch = item.Branch.Length > 0 ? item.Branch : "sem branch ainda";
        Detalhe = item.Estado == EstadoDoItem.Ignorada ? $"ignorada em {item.IgnoradaEm} (estava \"{ItemBeta.NomeDoEstado(item.EstadoAntes)}\")"
            : item.Estado == EstadoDoItem.Finalizada ? $"finalizada em {item.FinalizadaEm}: {item.FinalizadaMotivo}"
            : item.Erro.Length > 0 ? item.Erro : item.Motivo;
        Origem = item.Motivo;
        Quando = item.TransicaoEm.Length > 0 ? "no status desde " + item.TransicaoEm : item.DetectadaEm;
        Projetos = [.. item.Projetos.Where(p => p.Explicito || p.Gera || p.TemBranch || p.Complemento).Select(p => Projeto(p, item.Estado))];
        Prontos = Projetos.Count(p => p.Pronto);
        Progresso = $"{Prontos}/{Projetos.Count}";
        (Aviso, AvisoTom) = AvisoDe(item);
        Historico = HistoricoDe(item);
        PodeGerar = item.Estado is EstadoDoItem.Pronta or EstadoDoItem.Falhou or EstadoDoItem.Concluida;
        PodeCancelar = item.Estado is EstadoDoItem.NaFila or EstadoDoItem.Gerando;
        PodeIgnorar = item.Estado is not (EstadoDoItem.Gerando or EstadoDoItem.NaFila or EstadoDoItem.Ignorada or EstadoDoItem.Finalizada);
        PodeFinalizar = PodeIgnorar;
        PodeRemover = !PodeCancelar;
        TextoGerar = item.Estado switch { EstadoDoItem.Pronta => "Gerar beta", EstadoDoItem.Gerando or EstadoDoItem.NaFila => "Gerando...", _ => "Gerar de novo" };
        Gerar = new RelayCommand(() => acao("gerar", Chave));
        Cancelar = new RelayCommand(() => acao("cancelar", Chave));
        Ignorar = new RelayCommand(() => acao("ignorar", Chave));
        Finalizar = new RelayCommand(() => acao("finalizar", Chave));
        Remover = new RelayCommand(() => acao("remover", Chave));
        Reverter = new RelayCommand(() => acao("reverter", Chave));
        Reabrir = new RelayCommand(() => acao("reabrir", Chave));
    }

    public string Chave { get; }

    public string Titulo { get; }

    public EstadoDoItem Estado { get; }

    public string EstadoTexto { get; }

    public TomDeCor Tom { get; }

    public bool ComProblema => Tom == TomDeCor.Vermelho;

    public bool Gerando => Estado is EstadoDoItem.Gerando or EstadoDoItem.NaFila;

    public string Branch { get; }

    public string Detalhe { get; }

    /// <summary>Por que a atividade entrou (comentário seu, subtask Coding...).</summary>
    public string Origem { get; }

    public string Quando { get; }

    public IReadOnlyList<ProjetoDoCartao> Projetos { get; }

    public int Prontos { get; }

    /// <summary>"3/5": repositórios prontos em relação aos que a atividade usa.</summary>
    public string Progresso { get; }

    public string Aviso { get; }

    public TomDeCor AvisoTom { get; }

    public bool TemAviso => Aviso.Length > 0;

    public IReadOnlyList<string> Historico { get; }

    public bool PodeGerar { get; }

    public bool PodeCancelar { get; }

    public bool PodeIgnorar { get; }

    public bool PodeFinalizar { get; }

    public bool PodeRemover { get; }

    public string TextoGerar { get; }

    public ICommand Gerar { get; }

    public ICommand Cancelar { get; }

    public ICommand Ignorar { get; }

    public ICommand Finalizar { get; }

    public ICommand Remover { get; }

    public ICommand Reverter { get; }

    public ICommand Reabrir { get; }

    static string TextoDoEstado(EstadoDoItem e) => e switch
    {
        EstadoDoItem.Problema => "Com problema",
        EstadoDoItem.Aguardando => "Aguardando",
        EstadoDoItem.Gerando => "Gerando...",
        _ => ItemBeta.NomeDoEstado(e),
    };

    /// <summary>Cores do protótipo: pronta verde, aguardando âmbar, gerando azul-ciano, com problema vermelho.</summary>
    static TomDeCor TomDoEstado(EstadoDoItem e) => e switch
    {
        EstadoDoItem.Pronta or EstadoDoItem.Concluida => TomDeCor.Verde,
        EstadoDoItem.Problema or EstadoDoItem.Falhou => TomDeCor.Vermelho,
        EstadoDoItem.Gerando or EstadoDoItem.NaFila => TomDeCor.Azul,
        EstadoDoItem.Aguardando => TomDeCor.Amarelo,
        _ => TomDeCor.Neutro,
    };

    static (string, TomDeCor) AvisoDe(ItemBeta item)
    {
        if (item.Estado is EstadoDoItem.Ignorada or EstadoDoItem.Finalizada) return ("", TomDeCor.Amarelo);
        if (item.Erro.Length > 0) return (item.Erro, TomDeCor.Vermelho);
        var faltam = item.Projetos.Where(p => p.Explicito && !p.Gera).Select(p => p.Nome).ToList();
        return faltam.Count == 0 || item.Branch.Length == 0
            ? ("", TomDeCor.Amarelo)
            : ($"A branch {item.Branch} não foi achada em: {string.Join(", ", faltam)} (citado no comentário).", TomDeCor.Amarelo);
    }

    static List<string> HistoricoDe(ItemBeta item)
    {
        var h = new List<string>();
        if (item.TransicaoEm.Length > 0) h.Add($"{item.TransicaoEm} — entrou no status monitorado");
        if (item.DetectadaEm.Length > 0) h.Add($"{item.DetectadaEm} — o monitor consultou o Jira e achou a atividade" + (item.Motivo.Length > 0 ? $" ({item.Motivo})" : ""));
        if (item.GeradaEm.Length > 0) h.Add($"{item.GeradaEm} — beta gerada");
        if (item.IgnoradaEm.Length > 0) h.Add($"{item.IgnoradaEm} — ignorada");
        if (item.FinalizadaEm.Length > 0) h.Add($"{item.FinalizadaEm} — finalizada: {item.FinalizadaMotivo}");
        return h;
    }

    static ProjetoDoCartao Projeto(ProjetoDoItem p, EstadoDoItem estadoDoItem)
    {
        var nome = p.Complemento && p.Gera ? $"{p.Nome} (tag {p.Tag})" : p.Nome;
        var (marca, tom, situacao) = p.Resultado switch
        {
            ResultadoGerado.Ok => ("✓", TomDeCor.Verde, "pronto"),
            ResultadoGerado.Falhou => ("✕", TomDeCor.Vermelho, "problema"),
            ResultadoGerado.Gerando => ("…", TomDeCor.Azul, "gerando"),
            ResultadoGerado.Pulado => ("–", TomDeCor.Neutro, "pulado"),
            _ when p.Gera => ("✓", TomDeCor.Verde, "pronto"),
            _ when p.Explicito && estadoDoItem != EstadoDoItem.Aguardando => ("✕", TomDeCor.Vermelho, "problema"),
            _ => ("…", TomDeCor.Amarelo, "aguardando"),
        };
        return new ProjetoDoCartao(nome, p.Detalhe, marca, tom, situacao, situacao == "pronto");
    }
}
