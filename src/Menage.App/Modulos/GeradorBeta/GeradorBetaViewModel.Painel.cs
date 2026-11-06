using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.Beta;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Painel de andamento: fila em ordem, percentual, título e detalhe do que está acontecendo.</summary>
public sealed partial class GeradorBetaViewModel
{
    [ObservableProperty]
    double _percentual;

    [ObservableProperty]
    string _tituloDoPainel = "Nenhum projeto selecionado";

    [ObservableProperty]
    string _detalheDoPainel = "";

    [ObservableProperty]
    string _subDoAnel = "aguardando";

    [ObservableProperty]
    bool _painelComErro;

    /// <summary>Projetos marcados, na ordem de execução.</summary>
    public ObservableCollection<LinhaDeBeta> Fila { get; } = [];

    partial void OnExecutandoChanged(bool value) => AtualizarPainel();

    void MontarFila()
    {
        Fila.Clear();
        foreach (var nome in _ordem)
            if (Linhas.FirstOrDefault(l => l.Nome == nome) is { } linha) Fila.Add(linha);
        AtualizarPainel();
    }

    static bool Terminou(LinhaDeBeta l) => l.Fase is FaseDaGeracao.Ok or FaseDaGeracao.Erro or FaseDaGeracao.Interrompido;

    void AtualizarPainel()
    {
        var total = Fila.Count;
        var feitos = Fila.Count(Terminou);
        var falhas = Fila.Count(l => l.Fase is FaseDaGeracao.Erro or FaseDaGeracao.Interrompido);
        var atual = Fila.FirstOrDefault(l => l.Fase is FaseDaGeracao.Executando or FaseDaGeracao.Repetindo);
        Percentual = total == 0 ? 0 : 100.0 * feitos / total;
        PainelComErro = falhas > 0;
        var modo = Deploy ? "deploy" : "install";

        if (Executando && atual is not null)
            (TituloDoPainel, DetalheDoPainel, SubDoAnel) = ($"Gerando {atual.Nome}", $"{feitos + 1} de {total}  •  {atual.Comando}", "executando");
        else if (Executando)
            (TituloDoPainel, DetalheDoPainel, SubDoAnel) = ("Preparando a geração", $"{total} projeto(s) na fila", "preparando");
        else if (total > 0 && feitos == total)
            (TituloDoPainel, DetalheDoPainel, SubDoAnel) = falhas > 0
                ? ("Concluído com falha", $"{falhas} projeto(s) falharam: veja o log em vermelho.", "com falha")
                : ("Beta gerado", $"{total} projeto(s) gerados em modo {modo}.", "concluído");
        else if (total == 0)
            (TituloDoPainel, DetalheDoPainel, SubDoAnel) = ("Nenhum projeto selecionado", "Marque projetos à esquerda; a ordem de marcação é a ordem de execução.", "aguardando");
        else
            (TituloDoPainel, DetalheDoPainel, SubDoAnel) = ($"{total} projeto(s) na fila", $"{modo}  •  {string.Join(" → ", Fila.Select(l => l.Nome))}", "aguardando");
    }
}
