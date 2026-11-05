using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Menage.App.Controles;
using Menage.Core.BetaAutomatica;

namespace Menage.App.Modulos.BetaAutomatica;

public sealed record ProjetoDoCartao(string Nome, string Detalhe, string Marca, TomDeCor Tom);

/// <summary>Um cartão da lista (uma atividade). Só apresenta: as regras estão no <see cref="ServicoDeBetaAutomatica"/>.</summary>
public sealed class CartaoDeBeta
{
    public CartaoDeBeta(ItemBeta item, Action<string, string> acao)
    {
        Chave = item.Chave;
        Titulo = item.Titulo;
        Estado = item.Estado;
        EstadoTexto = ItemBeta.NomeDoEstado(item.Estado);
        Tom = TomDoEstado(item.Estado);
        Branch = item.Branch.Length > 0 ? item.Branch : "sem branch ainda";
        Detalhe = item.Estado == EstadoDoItem.Ignorada ? $"ignorada em {item.IgnoradaEm} (estava \"{ItemBeta.NomeDoEstado(item.EstadoAntes)}\")"
            : item.Estado == EstadoDoItem.Finalizada ? $"finalizada em {item.FinalizadaEm}: {item.FinalizadaMotivo}"
            : item.Erro.Length > 0 ? item.Erro : item.Motivo;
        Quando = item.TransicaoEm.Length > 0 ? "no status desde " + item.TransicaoEm : item.DetectadaEm;
        Projetos = [.. item.Projetos.Where(p => p.Explicito || p.Gera || p.TemBranch || p.Complemento).Select(Projeto)];
        PodeGerar = item.Estado is EstadoDoItem.Pronta or EstadoDoItem.Falhou or EstadoDoItem.Concluida;
        PodeCancelar = item.Estado is EstadoDoItem.NaFila or EstadoDoItem.Gerando;
        PodeIgnorar = item.Estado is not (EstadoDoItem.Gerando or EstadoDoItem.NaFila or EstadoDoItem.Ignorada or EstadoDoItem.Finalizada);
        PodeFinalizar = PodeIgnorar;
        PodeRemover = !PodeCancelar;
        TextoGerar = item.Estado == EstadoDoItem.Pronta ? "Gerar beta" : "Gerar de novo";
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

    public string Branch { get; }

    public string Detalhe { get; }

    public string Quando { get; }

    public IReadOnlyList<ProjetoDoCartao> Projetos { get; }

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

    static TomDeCor TomDoEstado(EstadoDoItem e) => e switch
    {
        EstadoDoItem.Pronta => TomDeCor.Verde,
        EstadoDoItem.Concluida => TomDeCor.Verde,
        EstadoDoItem.Problema => TomDeCor.Laranja,
        EstadoDoItem.Falhou => TomDeCor.Vermelho,
        EstadoDoItem.Gerando => TomDeCor.Azul,
        EstadoDoItem.NaFila => TomDeCor.Azul,
        EstadoDoItem.Aguardando => TomDeCor.Amarelo,
        _ => TomDeCor.Neutro,
    };

    static ProjetoDoCartao Projeto(ProjetoDoItem p)
    {
        var (marca, tom) = p.Resultado switch
        {
            ResultadoGerado.Ok => ("✔", TomDeCor.Verde),
            ResultadoGerado.Falhou => ("✖", TomDeCor.Vermelho),
            ResultadoGerado.Gerando => ("…", TomDeCor.Azul),
            ResultadoGerado.Pulado => ("–", TomDeCor.Neutro),
            _ => p.Gera ? ("●", TomDeCor.Verde) : p.Explicito ? ("✖", TomDeCor.Laranja) : ("○", TomDeCor.Neutro),
        };
        var nome = p.Complemento && p.Gera ? $"{p.Nome} (tag {p.Tag})" : p.Nome;
        return new ProjetoDoCartao(nome, p.Detalhe, marca, tom);
    }
}
