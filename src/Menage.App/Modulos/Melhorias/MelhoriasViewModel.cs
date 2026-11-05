using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Menage.App.Controles;
using Menage.App.Modulos.GeradorBeta;
using Menage.Core.Interacao;
using Menage.Core.Melhorias;

namespace Menage.App.Modulos.Melhorias;

public sealed record TagComCor(string Nome, int Total, TomDeCor Tom, bool Ligada);

public sealed record VisaoNaLista(string Nome, string Consulta, string Total, bool Salva, bool Ligada);

public sealed record LinhaDeProjetoDaMelhoria(string Projeto, string Esperada, string Atual, string Base, string Situacao, int Pendencias, TomDeCor Tom);

public sealed record LinhaDeCommit(string Projeto, string Hash, string Autor, string Quando, string Assunto);

public sealed record CartaoDePerfil(string Id, string Nome, string Tipo, bool AoAtivar, bool EmUso);

/// <summary>Um item da lista da esquerda.</summary>
public sealed class ItemDeMelhoria(Melhoria m, bool ativa, TomDeCor tomDaFaixa, IReadOnlyList<TagComCor> tags)
{
    public Melhoria Melhoria { get; } = m;

    public string Id => Melhoria.Id;

    public string Titulo => Melhoria.Titulo.Length > 0 ? Melhoria.Titulo : "(sem título)";

    public string Status => Melhoria.NomeDoStatus(Melhoria.Status);

    public bool Ativa { get; } = ativa;

    public TomDeCor Faixa { get; } = tomDaFaixa;

    public IReadOnlyList<TagComCor> Tags { get; } = tags;

    public string Resumo => $"{Melhoria.Branches.Count} projeto(s) · {Status}";
}

/// <summary>Estado do dashboard de Melhorias. As regras (busca, ativação, tags) estão no Core; aqui só se apresenta e se pede confirmação.</summary>
public sealed partial class MelhoriasViewModel : ObservableObject
{
    static readonly TomDeCor[] Automaticas = [TomDeCor.Azul, TomDeCor.Verde, TomDeCor.Amarelo, TomDeCor.Laranja, TomDeCor.GrupoWeb, TomDeCor.GrupoFw, TomDeCor.GrupoApp, TomDeCor.GrupoTc, TomDeCor.Destaque];

    readonly RepositorioDeMelhorias _repositorio;
    readonly AtivadorDeMelhorias _ativador;
    readonly CommitsDaMelhoria _commits;
    readonly IContextoDeBanco _banco;
    readonly IServicoDeDialogos _dialogos;
    readonly IServicoDeJanelas _janelas;
    bool _atualizando;

    [ObservableProperty]
    string _consulta = "";

    [ObservableProperty]
    ItemDeMelhoria? _selecionada;

    [ObservableProperty]
    string _mensagem = "";

    public MelhoriasViewModel(
        RepositorioDeMelhorias repositorio, AtivadorDeMelhorias ativador, CommitsDaMelhoria commits, IContextoDeBanco banco,
        IServicoDeDialogos dialogos, IServicoDeJanelas janelas)
    {
        (_repositorio, _ativador, _commits, _banco, _dialogos, _janelas) = (repositorio, ativador, commits, banco, dialogos, janelas);
        _consulta = repositorio.Config.Consulta;
    }

    public ObservableCollection<ItemDeMelhoria> Lista { get; } = [];

    public ObservableCollection<VisaoNaLista> Visoes { get; } = [];

    public ObservableCollection<TagComCor> Tags { get; } = [];

    public ObservableCollection<LinhaDeProjetoDaMelhoria> Projetos { get; } = [];

    public ObservableCollection<LinhaDeCommit> Commits { get; } = [];

    public ObservableCollection<CartaoDePerfil> Perfis { get; } = [];

    public bool VersaoConfLigada => _repositorio.Config.VersaoConf.Ativo;

    public bool SqliteLigado => _repositorio.Config.Sqlite.Ativo;

    public string SqliteCaminho => _repositorio.Config.Sqlite.Caminho;

    public string AtivaTexto => _repositorio.Config.AtivaMelhoria is { } a ? $"Em andamento: {a.Id} {a.Titulo}".Trim() : "Nenhuma melhoria ativa";

    public bool TemAtiva => _repositorio.Config.AtivaMelhoria is not null;

    public bool TemSelecionada => Selecionada is not null;

    public bool SelecionadaEhAtiva => Selecionada?.Ativa == true;

    public bool PodeAtivar => Selecionada is { Ativa: false };

    public string Cabecalho => Selecionada is null ? "" : $"{Selecionada.Id}  {Selecionada.Titulo}";

    public string NotasDaSelecionada => Selecionada?.Melhoria.Notas ?? "";

    public int PendenciasTotal => Projetos.Sum(p => p.Pendencias);

    public string ResumoDaLista => $"{Lista.Count} de {_repositorio.Config.Lista.Count} melhoria(s)";

    public string ResumoDosProjetos => $"{Projetos.Count} projeto(s) · {Projetos.Count(p => p.Tom == TomDeCor.Verde)} na branch certa · {Projetos.Count(p => p.Tom != TomDeCor.Verde)} fora · {PendenciasTotal} pendência(s)";

    partial void OnConsultaChanged(string value)
    {
        if (_atualizando) return;
        _repositorio.LembrarConsulta(value);
        Recarregar();
    }

    partial void OnSelecionadaChanged(ItemDeMelhoria? value)
    {
        OnPropertyChanged(nameof(TemSelecionada));
        OnPropertyChanged(nameof(SelecionadaEhAtiva));
        OnPropertyChanged(nameof(PodeAtivar));
        OnPropertyChanged(nameof(Cabecalho));
        OnPropertyChanged(nameof(NotasDaSelecionada));
        _ = CarregarDetalheAsync(value);
    }

    public TomDeCor CorDaTag(string tag) =>
        _repositorio.Config.CoresTags.TryGetValue(tag, out var chave) && Enum.TryParse<TomDeCor>(chave, ignoreCase: true, out var tom)
            ? tom
            : Automaticas[(int)(tag.Aggregate(17u, (h, c) => unchecked((h * 31) + c)) % (uint)Automaticas.Length)];

    /// <summary>Relê tudo do repositório e mantém a seleção.</summary>
    public void Recarregar()
    {
        _atualizando = true;
        var config = _repositorio.Config;
        var idAnterior = Selecionada?.Id;
        OnPropertyChanged(nameof(VersaoConfLigada));
        OnPropertyChanged(nameof(SqliteLigado));
        OnPropertyChanged(nameof(SqliteCaminho));
        OnPropertyChanged(nameof(AtivaTexto));
        OnPropertyChanged(nameof(TemAtiva));

        Lista.Clear();
        foreach (var m in _repositorio.Buscar(Consulta).OrderByDescending(m => m.Id.Equals(config.Ativa, StringComparison.OrdinalIgnoreCase)).ThenByDescending(m => m.UltimoUso))
            Lista.Add(Item(m));

        PreencherVisoes();
        Tags.Clear();
        foreach (var (nome, total) in config.TagsComContagem()) Tags.Add(new TagComCor(nome, total, CorDaTag(nome), FiltroDeMelhorias.TemTermo(Consulta, "tag", nome)));
        _atualizando = false;
        Selecionada = Lista.FirstOrDefault(i => i.Id.Equals(idAnterior, StringComparison.OrdinalIgnoreCase)) ?? Lista.FirstOrDefault();
        OnPropertyChanged(nameof(ResumoDaLista));
    }

    ItemDeMelhoria Item(Melhoria m)
    {
        var tags = m.Tags.Select(t => new TagComCor(t, 0, CorDaTag(t), false)).ToList();
        var faixa = tags.Count > 0 ? tags[0].Tom : m.Status switch { Melhoria.Pausada => TomDeCor.Amarelo, Melhoria.Concluida => TomDeCor.Neutro, _ => TomDeCor.Azul };
        return new ItemDeMelhoria(m, m.Id.Equals(_repositorio.Config.Ativa, StringComparison.OrdinalIgnoreCase), faixa, tags);
    }

    void PreencherVisoes()
    {
        Visoes.Clear();
        foreach (var v in _repositorio.VisoesProntas())
            Visoes.Add(new VisaoNaLista(v.Nome, v.Consulta, v.Total.ToString(CultureInfo.InvariantCulture), false, Consulta.Trim() == v.Consulta));
        foreach (var f in _repositorio.Config.Filtros)
            Visoes.Add(new VisaoNaLista("★ " + f.Nome, f.Consulta, "", true, Consulta.Trim() == f.Consulta));
    }

    async Task CarregarDetalheAsync(ItemDeMelhoria? item)
    {
        Projetos.Clear();
        Commits.Clear();
        Perfis.Clear();
        if (item is null) { OnPropertyChanged(nameof(ResumoDosProjetos)); return; }

        var m = item.Melhoria;
        foreach (var l in await _ativador.PreviaAsync(m))
        {
            var tom = l.Problema ? TomDeCor.Vermelho : l.JaEsta ? TomDeCor.Verde : TomDeCor.Laranja;
            Projetos.Add(new LinhaDeProjetoDaMelhoria(l.Projeto, l.Destino, l.Atual.Length > 0 ? l.Atual : "—", l.Base, l.Situacao, l.Pendencias, tom));
        }

        var aplicar = m.PerfilParaAplicar(i => _banco.Achar(i) is not null);
        var emUso = _banco.AtivoNoContexto()?.Id;
        foreach (var id in m.Perfis)
            if (_banco.Achar(id) is { } p) Perfis.Add(new CartaoDePerfil(p.Id, p.Nome, p.Tipo, aplicar == p.Id, emUso == p.Id));

        OnPropertyChanged(nameof(ResumoDosProjetos));
        OnPropertyChanged(nameof(PendenciasTotal));
        if (Selecionada != item) return;
        foreach (var c in await _commits.ListarAsync(m, 100))
            Commits.Add(new LinhaDeCommit(c.Projeto, c.Hash, c.Autor, c.Quando.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), c.Assunto));
    }

    // ---- busca

    public void AlternarTermo(string campo, string valor, bool exclusivo = false) => Consulta = FiltroDeMelhorias.Alternar(Consulta, campo, valor, exclusivo);

    public void AplicarVisao(string consulta) => Consulta = consulta;

    public void LimparBusca() => Consulta = "";

    public async Task SalvarVisaoAsync()
    {
        if (Consulta.Trim().Length == 0) { Mensagem = "Escreva uma busca antes de salvar a visão."; return; }
        if (await _janelas.AbrirDialogoAsync(JanelaDeTexto.Id, new PedidoDeTexto("Salvar visão", "Nome da visão", "")) is not string nome || nome.Length == 0) return;
        Mensagem = _repositorio.SalvarVisao(nome, Consulta) is { EhOk: false } r ? r.Erro.ToString() : "";
        Recarregar();
    }

    public void RemoverVisao(string nome)
    {
        _repositorio.RemoverVisao(nome.TrimStart('★', ' '));
        Recarregar();
    }

    // ---- tags

    public async Task RenomearTagAsync(string tag)
    {
        if (await _janelas.AbrirDialogoAsync(JanelaDeTexto.Id, new PedidoDeTexto("Renomear tag", $"Novo nome para \"{tag}\" (em todas as melhorias)", tag)) is not string novo || novo.Length == 0) return;
        _repositorio.RenomearTag(tag, novo);
        Recarregar();
    }

    public async Task RemoverTagAsync(string tag)
    {
        if (!await _dialogos.ConfirmarAsync(new Previa("Remover tag", $"Remover a tag \"{tag}\" de todas as melhorias?", ["Também some dos filtros salvos que a usam."]))) return;
        _repositorio.RemoverTag(tag);
        Recarregar();
    }

    public void DefinirCorDaTag(string tag, string? cor)
    {
        _repositorio.DefinirCorDaTag(tag, cor);
        Recarregar();
    }

    // ---- ações

    public async Task NovaAsync() => await EditarAsync(null);

    public async Task EditarAsync(Melhoria? m)
    {
        if (await _janelas.AbrirDialogoAsync(JanelaEditorDeMelhoria.Id, new PedidoDeEdicaoDeMelhoria(m)) is not ResultadoDoEditor r) return;
        if (r.MensagemDoSqlite is { Length: > 0 } sq) Mensagem = sq;
        if (r.Nova && _repositorio.Achar(r.Id) is { } nova)
        {
            if (r.CriarBranches)
            {
                var criar = await _ativador.CriarBranchesAsync(nova, r.Publicar);
                await MostrarAsync(criar);
                if (criar.Erros == 0) await ExecutarAtivacaoAsync(nova);
            }
        }

        Recarregar();
        Selecionada = Lista.FirstOrDefault(i => i.Id.Equals(r.Id, StringComparison.OrdinalIgnoreCase)) ?? Selecionada;
    }

    public async Task AtivarAsync()
    {
        if (Selecionada?.Melhoria is not { } m) return;
        var previa = await _ativador.PreviaAsync(m);
        var comPendencia = previa.Count(l => l.Pendencias > 0 && !l.JaEsta);
        var detalhes = previa.Select(l => $"{l.Projeto}: {(l.Atual.Length > 0 ? l.Atual : "—")} → {l.Destino}  ({l.Situacao}{(l.Pendencias > 0 ? $"; {l.Pendencias} pendência(s)" : "")})").ToList();
        if (comPendencia > 0) detalhes.Add("As pendências são guardadas em stash automático (marcado com a branch) e voltam ao retornar a ela, arquivos novos inclusos.");
        detalhes.Add(m.Perfis.Count > 0 ? "O perfil de banco da melhoria será aplicado." : "Banco: nenhum perfil associado; mantido como está.");
        var titulo = comPendencia > 0 ? "Ativar e guardar pendências" : "Ativar melhoria";
        if (!await _dialogos.ConfirmarAsync(new Previa(titulo, $"Ativar {m.Id}? Projetos fora da melhoria ficam como estão.", detalhes))) return;
        await ExecutarAtivacaoAsync(m);
        Recarregar();
    }

    async Task ExecutarAtivacaoAsync(Melhoria m) => await MostrarAsync(await _ativador.AtivarAsync(m, aplicarBanco: m.Perfis.Count > 0));

    public async Task DesvincularAsync()
    {
        var linhas = await _ativador.DesvincularPreviaAsync();
        if (!await _dialogos.ConfirmarAsync(new Previa("Desvincular a melhoria", $"Desvincular {_repositorio.Config.Ativa}?", linhas))) return;
        await MostrarAsync(await _ativador.DesvincularAsync(restaurarBanco: true));
        Recarregar();
    }

    public async Task ExcluirAsync()
    {
        if (Selecionada?.Melhoria is not { } m) return;
        if (!await _dialogos.ConfirmarAsync(new Previa("Excluir melhoria", $"Excluir {m.Id} do cadastro?", ["As branches e os projetos não são tocados."]))) return;
        var r = _repositorio.Excluir(m.Id);
        Mensagem = r.EhOk ? "" : r.Erro.ToString();
        Recarregar();
    }

    public void DefinirPerfilAoAtivar(string perfilId)
    {
        if (Selecionada?.Melhoria is not { } m) return;
        m.PerfilAtivoId = perfilId;
        _repositorio.Salvar();
        _ = CarregarDetalheAsync(Selecionada);
    }

    public async Task AplicarPerfilAgoraAsync(string perfilId)
    {
        var r = await _banco.AplicarAsync(perfilId);
        Mensagem = r.EhOk ? "Perfil aplicado." : r.Erro.ToString();
        await CarregarDetalheAsync(Selecionada);
    }

    public void LigarVersaoConf(bool ligado)
    {
        Mensagem = _repositorio.ConfigurarVersaoConf(ligado) is { EhOk: false } r ? r.Erro.ToString() : "";
        OnPropertyChanged(nameof(VersaoConfLigada));
    }

    /// <summary>Devolve falso se faltar o caminho (a tela abre o seletor de arquivo).</summary>
    public bool LigarSqlite(bool ligado, string? caminho = null)
    {
        var r = _repositorio.ConfigurarSqlite(ligado, caminho);
        Mensagem = r.EhOk ? "" : r.Erro.ToString();
        OnPropertyChanged(nameof(SqliteLigado));
        OnPropertyChanged(nameof(SqliteCaminho));
        return r.EhOk;
    }

    public void DefinirCaminhoDoSqlite(string caminho)
    {
        _repositorio.ConfigurarSqlite(_repositorio.Config.Sqlite.Ativo, caminho);
        OnPropertyChanged(nameof(SqliteCaminho));
    }

    async Task MostrarAsync(ResumoDeMelhoria resumo)
    {
        var simbolo = new Dictionary<NivelDeLinha, string> { [NivelDeLinha.Ok] = "✔", [NivelDeLinha.Info] = "•", [NivelDeLinha.Aviso] = "!", [NivelDeLinha.Erro] = "✖" };
        var texto = string.Join(Environment.NewLine, resumo.Linhas.Select(l => $"{simbolo[l.Nivel]} {l.Texto}"));
        Mensagem = resumo.Erros > 0 ? $"{resumo.Titulo}: {resumo.Erros} erro(s)." : $"{resumo.Titulo}: concluído.";
        await _dialogos.InformarAsync(resumo.Titulo, texto);
        await Application.Current.Dispatcher.InvokeAsync(Recarregar);
    }
}
