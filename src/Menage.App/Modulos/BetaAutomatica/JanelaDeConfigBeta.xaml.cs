using System.Windows;
using System.Windows.Controls;
using Menage.App.Controles;
using Menage.App.Modulos.GeradorBeta;
using Menage.Core.BetaAutomatica;
using Menage.Core.Interacao;

namespace Menage.App.Modulos.BetaAutomatica;

/// <summary>Configuração do monitor (Jira, status, intervalo, pasta dos clones, escopo do Tester, ordem dos projetos).</summary>
public partial class JanelaDeConfigBeta : JanelaBase, IJanelaComParametro, IJanelaComResultado
{
    public const string Id = "beta-auto.config";

    readonly IServicoDeJanelas _janelas;
    ServicoDeBetaAutomatica? _servico;

    public JanelaDeConfigBeta(ContextoDeJanela contexto, IServicoDeJanelas janelas) : base(contexto)
    {
        _janelas = janelas;
        InitializeComponent();
    }

    public object? Resultado { get; private set; }

    public void Receber(object? parametro)
    {
        if (parametro is not ServicoDeBetaAutomatica s) return;
        _servico = s;
        var c = s.Config;
        Url.Text = c.JiraUrl;
        Variavel.Text = c.VariavelDoToken;
        Chaves.Text = c.ChavesJira;
        Alvo.Text = c.StatusAlvo;
        Final.Text = c.StatusFinal;
        Intervalo.Text = c.IntervaloMin.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Pasta.Text = c.PastaTrabalho;
        Dev.Text = c.StatusDev;
        Revisao.Text = c.StatusRevisao;
        Tipos.Text = c.TiposCoding;
        CodeReview.Text = c.StatusCodeReview;
        Escopo.SelectedIndex = c.TesterEscopo.Equals("todas", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        PainelTester.Visibility = s.ModoTester ? Visibility.Visible : Visibility.Collapsed;
        PainelDev.Visibility = s.ModoTester ? Visibility.Collapsed : Visibility.Visible;
    }

    async void AoOrdem(object sender, RoutedEventArgs e)
    {
        if (_servico is null) return;
        var atual = OrdemDeProjetos.Efetiva(_servico.Config.OrdemProjetos);
        if (await _janelas.AbrirDialogoAsync(JanelaDeOrdem.Id, atual) is List<string> nova) _servico.Alterar(c => c.OrdemProjetos = nova);
    }

    void AoSalvar(object sender, RoutedEventArgs e)
    {
        if (_servico is null) return;
        if (!int.TryParse(Intervalo.Text, out var minutos) || minutos < 1) { Aviso.Text = "O intervalo precisa ser um número de minutos (1 ou mais)."; return; }
        if (Alvo.Text.Trim().Length == 0 || Pasta.Text.Trim().Length == 0 || Url.Text.Trim().Length == 0) { Aviso.Text = "Jira, status e pasta dos clones são obrigatórios."; return; }

        var resultado = _servico.Alterar(c =>
        {
            c.JiraUrl = Url.Text.Trim();
            c.VariavelDoToken = Variavel.Text.Trim();
            c.ChavesJira = Chaves.Text.Trim();
            c.StatusAlvo = Alvo.Text.Trim();
            c.StatusFinal = Final.Text.Trim();
            c.IntervaloMin = minutos;
            c.PastaTrabalho = Pasta.Text.Trim();
            c.StatusDev = Dev.Text.Trim();
            c.StatusRevisao = Revisao.Text.Trim();
            c.TiposCoding = Tipos.Text.Trim();
            c.StatusCodeReview = CodeReview.Text.Trim();
            if (Escopo.SelectedItem is ComboBoxItem { Tag: string t }) c.TesterEscopo = t;
        });
        if (!resultado.EhOk) { Aviso.Text = resultado.Erro.ToString(); return; }
        Resultado = true;
        Close();
    }
}
