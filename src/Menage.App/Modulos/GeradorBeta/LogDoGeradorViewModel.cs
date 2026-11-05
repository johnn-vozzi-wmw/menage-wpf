using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.Core.Beta;

namespace Menage.App.Modulos.GeradorBeta;

public sealed record LinhaDeLog(string Texto, TipoDeLinhaDoLog Tipo);

/// <summary>Log do projeto selecionado na área preta: lê só o que foi acrescentado, filtra "Somente erros" e acompanha a execução.</summary>
public sealed partial class LogDoGeradorViewModel : ObservableObject
{
    const int MaximoDeLinhas = 20000;

    readonly AcompanhaLog _acompanha = new();
    readonly List<LinhaDeLog> _todas = [];

    [ObservableProperty]
    string _titulo = "Log: (selecione um projeto)";

    [ObservableProperty]
    bool _somenteErros;

    [ObservableProperty]
    bool _acompanhar = true;

    public ObservableCollection<LinhaDeLog> Linhas { get; } = [];

    public string? Caminho => _acompanha.Caminho;

    public void Mostrar(LinhaDeBeta? linha)
    {
        if (linha is null) return;
        Titulo = $"Log: {linha.Nome}";
        if (linha.Log == _acompanha.Caminho) return;
        _acompanha.Abrir(linha.Log);
        _todas.Clear();
        Linhas.Clear();
        if (linha.Log is null) Linhas.Add(new LinhaDeLog("(ainda não executado nesta sessão)", TipoDeLinhaDoLog.Normal));
        Tique();
    }

    public void Tique()
    {
        foreach (var texto in _acompanha.Novas())
        {
            var linha = new LinhaDeLog(texto, ClassificadorDeLinha.Classificar(texto));
            _todas.Add(linha);
            if (Passa(linha)) Linhas.Add(linha);
        }

        if (_todas.Count > MaximoDeLinhas) Reconstruir(descartar: _todas.Count - MaximoDeLinhas);
    }

    partial void OnSomenteErrosChanged(bool value) => Reconstruir(0);

    void Reconstruir(int descartar)
    {
        if (descartar > 0) _todas.RemoveRange(0, descartar);
        Linhas.Clear();
        foreach (var l in _todas.Where(Passa)) Linhas.Add(l);
    }

    bool Passa(LinhaDeLog l) => !SomenteErros || l.Tipo == TipoDeLinhaDoLog.Erro;
}
