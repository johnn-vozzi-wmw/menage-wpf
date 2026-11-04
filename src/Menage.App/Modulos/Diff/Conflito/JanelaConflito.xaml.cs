using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Menage.Core.Diff;
using Menage.Core.Interacao;
using Wpf.Ui.Controls;

namespace Menage.App.Modulos.Diff.Conflito;

/// <summary>Arquivos em conflito do repositório à esquerda, o escolhido sendo resolvido à direita.</summary>
public partial class JanelaConflito : FluentWindow
{
    readonly ServicoConflito _servico;
    readonly IServicoDeDialogos _dialogos;
    readonly IPerguntaDeLado _pergunta;
    readonly string _repo;
    bool _podeFechar;
    string? _mostrado;

    JanelaConflito(ServicoConflito servico, IServicoDeDialogos dialogos, IPerguntaDeLado pergunta, string repo)
    {
        (_servico, _dialogos, _pergunta, _repo) = (servico, dialogos, pergunta, repo);
        InitializeComponent();
        Animador = new AnimadorLista(HospedeiroLista, Lista, Conteudo, ColunaLista, 54, 280);
        Animador.Terminou += Lista.AoMudarEstado;
        Lista.Escolheu += a => _ = AbrirAsync(a);
        Lista.PediuAtualizar += () => _ = RecarregarAsync();
        Lista.PediuRecolher += Animador.Alternar;
        Loaded += async (_, _) =>
        {
            Painel.Rolagem.LigarJanela(this);
            Title = Barra.Title = $"Resolver conflito - {Path.GetFileName(repo.TrimEnd('\\', '/'))}   •   {(await _servico.LerLadosAsync(repo)).Operacao} em andamento";
            await RecarregarAsync();
        };
        Closed += (_, _) => Painel.Rolagem.Dispose();
    }

    public AnimadorLista Animador { get; }

    public PainelConflito PainelDoConflito => Painel;

    public ListaArquivos ListaDeArquivos => Lista;

    public sealed class Fabrica(ServicoConflito servico, IServicoDeDialogos dialogos, IPerguntaDeLado pergunta)
    {
        public JanelaConflito Criar(string repo) => new(servico, dialogos, pergunta, repo);
    }

    public async Task RecarregarAsync()
    {
        var lista = await _servico.ListarAsync(_repo);
        if (!lista.EhOk)
        {
            await _dialogos.MostrarErroAsync(lista.Erro);
            return;
        }
        _mostrado = null;
        var vazio = lista.Valor.Count == 0;
        Vazio.Text = vazio ? "Não há arquivos em conflito neste repositório." : "";
        Vazio.Visibility = vazio ? Visibility.Visible : Visibility.Collapsed;
        Painel.Visibility = vazio ? Visibility.Hidden : Visibility.Visible;
        BotaoSalvar.IsEnabled = !vazio;
        Lista.Definir(lista.Valor);
    }

    /// <summary>Grava o resultado, marca resolvido e passa para o próximo arquivo.</summary>
    public async Task SalvarAsync()
    {
        if (Painel.Arquivo is not { } arquivo) return;
        var resultado = await _servico.GravarAsync(_repo, arquivo, Painel.LinhasDoResultado());
        if (!resultado.EhOk)
        {
            await _dialogos.MostrarErroAsync(resultado.Erro);
            Painel.Ir(1);
            return;
        }
        Painel.Resultado.Editor.Document.UndoStack.ClearAll();
        await RecarregarAsync();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_podeFechar || !Painel.TemAlteracao) return;
        e.Cancel = true;
        _ = ConfirmarFechamentoAsync();
    }

    async Task AbrirAsync(ArquivoAlterado arquivo)
    {
        if (arquivo.Caminho == _mostrado) return;
        if (Painel.TemAlteracao && !await ConfirmarDescarteAsync())
        {
            Lista.Definir(((IEnumerable<ItemArquivo>)Lista.Lista.ItemsSource).Select(i => i.Arquivo).ToList(), _mostrado);
            return;
        }
        var forma = await _servico.ClassificarAsync(_repo, arquivo);
        if (forma == FormaDeResolver.Texto)
        {
            var carga = await _servico.CarregarAsync(_repo, arquivo.Caminho);
            if (!carga.EhOk)
            {
                await _dialogos.MostrarErroAsync(carga.Erro);
                return;
            }
            _mostrado = arquivo.Caminho;
            await Painel.MostrarAsync(carga.Valor);
            return;
        }
        await ResolverSemTextoAsync(arquivo, forma);
    }

    /// <summary>Binário ou excluído de um lado não dá para juntar: pergunta quem fica e já marca resolvido.</summary>
    async Task ResolverSemTextoAsync(ArquivoAlterado arquivo, FormaDeResolver forma)
    {
        var lados = await _servico.LerLadosAsync(_repo);
        var titulo = $"Resolver conflito - {arquivo.Caminho}";
        LadoEscolhido? lado = forma switch
        {
            FormaDeResolver.Excluir => LadoEscolhido.Excluir,
            FormaDeResolver.EscolherLado => await _pergunta.PerguntarAsync(titulo, $"{arquivo.Caminho} é binário: não dá para juntar as duas versões.",
                (lados.UsarEntrando, LadoEscolhido.Entrando), (lados.UsarAtual, LadoEscolhido.Atual)),
            _ => await _pergunta.PerguntarAsync(titulo, $"{arquivo.Caminho}: {ServicoConflito.Descricao(arquivo.CodigoStatus)}.",
                ("Manter o arquivo", LadoEscolhido.Manter), ("Excluir o arquivo", LadoEscolhido.Excluir)),
        };
        if (lado is not { } escolhido) return;
        var resultado = await _servico.ResolverAsync(_repo, arquivo, escolhido);
        if (!resultado.EhOk) await _dialogos.MostrarErroAsync(resultado.Erro);
        await RecarregarAsync();
    }

    Task<bool> ConfirmarDescarteAsync() =>
        _dialogos.ConfirmarAsync(new Previa("Resolver conflito", "Descartar as escolhas feitas neste arquivo?\n\nO arquivo continua em conflito, como estava.", []));

    async Task ConfirmarFechamentoAsync()
    {
        if (!await ConfirmarDescarteAsync()) return;
        _podeFechar = true;
        Close();
    }

    void AoTecla(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.B && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Animador.Alternar();
            e.Handled = true;
        }
        else if (Painel.Tecla(e)) e.Handled = true;
    }

    void AoSalvar(object sender, RoutedEventArgs e) => _ = SalvarAsync();

    void AoFechar(object sender, RoutedEventArgs e) => Close();
}
