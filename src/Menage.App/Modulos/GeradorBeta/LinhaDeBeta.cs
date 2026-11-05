using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.Beta;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Uma linha da tabela do Gerador: o projeto, a marcação, a ordem e o andamento da geração.</summary>
public sealed partial class LinhaDeBeta(ProjetoDoGerador projeto, string? pasta, string branch) : ObservableObject
{
    [ObservableProperty]
    bool _marcado;

    [ObservableProperty]
    string _ordem = "";

    [ObservableProperty]
    string _comando = "";

    [ObservableProperty]
    FaseDaGeracao _fase = FaseDaGeracao.NaoSelecionado;

    [ObservableProperty]
    string _tempo = "";

    [ObservableProperty]
    string _resumo = "";

    [ObservableProperty]
    string _branch = branch;

    [ObservableProperty]
    string? _pasta = pasta;

    public ProjetoDoGerador Projeto { get; } = projeto;

    public string Nome => Projeto.Nome;

    public string Grupo => Projeto.Grupo;

    public string Jdk => Projeto.Jdk;

    public bool Encontrado => Pasta is not null;

    public string? Log { get; set; }

    public DateTime? Inicio { get; set; }

    public string Status => Fase switch
    {
        FaseDaGeracao.NaFila => "… Na fila",
        FaseDaGeracao.Executando => "▶ Executando",
        FaseDaGeracao.Repetindo => "↻ Repetindo (surefire)",
        FaseDaGeracao.Ok => "✔ Sucesso",
        FaseDaGeracao.Erro => "✖ Erro",
        FaseDaGeracao.Interrompido => "✖ Interrompido",
        FaseDaGeracao.NaoExecutado => "Não executado",
        _ => Encontrado ? "" : "Pasta não encontrada",
    };

    partial void OnFaseChanged(FaseDaGeracao value) => OnPropertyChanged(nameof(Status));

    partial void OnPastaChanged(string? value) => OnPropertyChanged(nameof(Encontrado));

    public void Aplicar(StatusDoProjeto s)
    {
        Fase = s.Estado;
        if (s.Log is not null) Log = s.Log;
        if (s.Inicio is not null) Inicio = s.Inicio;
        if (s.Estado is FaseDaGeracao.Ok or FaseDaGeracao.Erro or FaseDaGeracao.Interrompido) Tempo = Formatar(s.Tempo);
        Resumo = s.Resumo;
    }

    public void Reiniciar(bool naFila)
    {
        Fase = naFila ? FaseDaGeracao.NaFila : FaseDaGeracao.NaoSelecionado;
        Tempo = "";
        Resumo = "";
        Inicio = null;
    }

    public void AtualizarTempoCorrido()
    {
        if (Fase is FaseDaGeracao.Executando or FaseDaGeracao.Repetindo && Inicio is { } i) Tempo = Formatar(DateTime.Now - i);
    }

    public static string Formatar(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
}
