using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Menage.Core.Configuracoes;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.Configuracoes;

/// <summary>Um item que o Git Manager antigo tem e o Menage pode importar. Só vem marcado o que foi entendido.</summary>
public sealed partial class ItemParaImportar(AchadoDeMigracao achado) : ObservableObject
{
    public AchadoDeMigracao Achado => achado;

    public string Titulo => achado.Titulo;

    public string Detalhe => achado.Resumo;

    public bool Encontrado => achado.Encontrado;

    [ObservableProperty]
    bool _marcado = achado.Encontrado;
}

/// <summary>Seção "Importar do Git Manager": análise somente-leitura e importação item por item, sempre por pedido do usuário.</summary>
public sealed partial class ConfiguracoesViewModel
{
    const string PastaSugerida = @"C:\wmw";

    readonly MigradorDoGitManager _migrador;

    public ObservableCollection<ItemParaImportar> Achados { get; } = [];

    public ObservableCollection<string> InstalacoesAntigas { get; } = [];

    [ObservableProperty]
    string _pastaAntiga = "";

    [ObservableProperty]
    string _relatorioDaImportacao = "";

    public bool TemAchados => Achados.Count > 0;

    public bool TemInstalacoes => InstalacoesAntigas.Count > 0;

    public bool TemRelatorio => RelatorioDaImportacao.Length > 0;

    partial void OnRelatorioDaImportacaoChanged(string value) => OnPropertyChanged(nameof(TemRelatorio));

    [RelayCommand]
    void ProcurarInstalacoes()
    {
        InstalacoesAntigas.Clear();
        foreach (var p in LeitorDoGitManager.Localizar(PastaSugerida)) InstalacoesAntigas.Add(p);
        OnPropertyChanged(nameof(TemInstalacoes));
        Avisar(InstalacoesAntigas.Count == 0
            ? $"Nenhuma instalação do Git Manager encontrada em {PastaSugerida}. Escolha a pasta manualmente."
            : $"{InstalacoesAntigas.Count} pasta(s) com configurações do Git Manager encontrada(s).");
    }

    [RelayCommand]
    void Analisar()
    {
        Achados.Clear();
        RelatorioDaImportacao = "";
        if (!Directory.Exists(PastaAntiga)) { Avisar("Escolha a pasta onde está o Git Manager antigo (a do perfil.json e dos .cfg).", true); return; }
        foreach (var a in _migrador.Analisar(PastaAntiga)) Achados.Add(new ItemParaImportar(a));
        OnPropertyChanged(nameof(TemAchados));
        var achados = Achados.Count(a => a.Encontrado);
        Avisar(achados == 0
            ? "Nada que o Menage entenda nesta pasta."
            : $"{achados} item(ns) podem ser importados. Nada foi alterado ainda; a pasta antiga só é lida.");
    }

    [RelayCommand]
    async Task ImportarAsync()
    {
        var marcados = Achados.Where(a => a.Marcado && a.Encontrado).ToList();
        if (marcados.Count == 0) { Avisar("Marque ao menos um item para importar.", true); return; }
        var previa = new Previa("Importar configurações do Git Manager?",
            $"De: {PastaAntiga}",
            [.. marcados.Select(m => $"{m.Titulo}: {m.Detalhe}"), "Os arquivos antigos não são alterados.", "As configurações atuais dos mesmos itens serão substituídas."]);
        if (!await _dialogos.ConfirmarAsync(previa)) return;

        var relatorio = _migrador.Aplicar(PastaAntiga, marcados.Select(m => m.Achado.Item).ToList());
        if (relatorio.FetchAutomatico is not null || relatorio.SaidaRecolhida is not null)
            _preferencias.Alterar(p => p with { FetchAutomatico = relatorio.FetchAutomatico ?? p.FetchAutomatico, SaidaRecolhida = relatorio.SaidaRecolhida ?? p.SaidaRecolhida });
        var temaAplicado = AplicarTemaImportado(relatorio.TemaId);
        Carregar();

        var linhas = relatorio.Aplicados.Select(a => "✔ " + a).Concat(relatorio.Falhas.Select(f => "✖ " + f));
        RelatorioDaImportacao = string.Join(Environment.NewLine, temaAplicado is null ? linhas : linhas.Append(temaAplicado));
        Avisar(relatorio.Falhas.Count == 0 ? $"{relatorio.Aplicados.Count} item(ns) importado(s)." : $"{relatorio.Aplicados.Count} importado(s), {relatorio.Falhas.Count} com problema.", relatorio.Falhas.Count > 0);
    }

    // O Git Manager guardava só "escuro" ou "claro" nos primeiros tempos; ids de tema que o Menage não conhece são ignorados.
    string? AplicarTemaImportado(string? id)
    {
        if (id is null) return null;
        var destino = Temas.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));
        if (destino is null) return $"ℹ Tema \"{id}\" não existe no Menage; o tema atual foi mantido.";
        Tema = destino;
        return $"✔ Tema: {destino.Nome}";
    }
}
