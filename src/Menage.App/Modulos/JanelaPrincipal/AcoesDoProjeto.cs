using System.Diagnostics;
using System.IO;
using System.Windows;
using Menage.App.Modulos.Diff;
using Menage.Core;
using Menage.Core.Comandos;
using Menage.Core.Configuracoes;
using Menage.Core.Diff;
using Menage.Core.Interacao;
using Menage.Core.Operacoes;
using Menage.Core.Operacoes.DefinirOrigin;
using Menage.Core.Operacoes.Ferramentas;
using Menage.Core.Perfis;
using Menage.Core.Projetos;
using Menage.Core.Repos;
using Menage.Core.Saida;

namespace Menage.App.Modulos.JanelaPrincipal;

/// <summary>
/// O que o botão direito de um projeto faz: ações do dia a dia só nele (sem mexer nas marcações do usuário), diff das alterações,
/// pasta, terminal, copiar branch/hash/PR/URL e definir o origin.
/// </summary>
public sealed class AcoesDoProjeto(
    IProjetos projetos, CoordenadorDeOperacoes coordenador, ICatalogoDeComandos catalogo, IPerfilAtual perfil, AcoesGit acoes, SaidaDeOperacoes saida,
    IServicoDeDialogos dialogos, ServicoDeConfiguracoes configuracoes, JanelaDiff.Fabrica diff, CopiaDosMarcados copia, IGit git,
    DefinirOriginEmLote definirOrigin)
{
    /// <summary>Ações do dia a dia oferecidas "só neste projeto", na ordem do Git Manager: (id do catálogo, nome).</summary>
    public static readonly IReadOnlyList<(string Id, string Nome)> SoNeste =
    [
        ("git.fetch", "Fetch"), ("git.pull", "Pull"), ("git.sync", "Sync"), ("git.commit", "Commit"), ("git.push", "Push"), ("git.cherry-pick", "Cherry-pick"),
    ];

    public string? MotivoParaMarcar => coordenador.EmAndamento ? "Aguarde a operação terminar" : null;

    public bool TortoiseDisponivel => FerramentasExternasDeDiff.TortoiseProc(configuracoes.Atual.Caminhos.TortoiseGit) is not null;

    public string NomeDaFerramentaDeDiff => FerramentasDeDiff.Nome(FerramentasDeDiff.Normalizar(configuracoes.Atual.Git.FerramentaDeDiff));

    /// <summary>Por que a ação não pode rodar agora (nulo se pode): perfil, operação em curso ou comando que o catálogo ainda não tem.</summary>
    public string? MotivoDoComando(string id)
    {
        if (catalogo.Obter(id) is not { } comando) return "Em breve";
        if (!comando.PerfisQueLiberam.Contains(perfil.Atual)) return $"Disponível só no perfil {string.Join(" ou ", comando.PerfisQueLiberam)}";
        return coordenador.EmAndamento ? "Aguarde a operação terminar" : null;
    }

    /// <summary>Roda o comando só neste projeto: marca só ele, executa e devolve as marcações como estavam.</summary>
    public Task RodarSoNesteAsync(Projeto projeto, string idDoComando) => acoes.Proteger(async () =>
    {
        if (MotivoDoComando(idDoComando) is not null) return;
        await ComSomenteAsync(projeto, () => catalogo.Obter(idDoComando)!.ExecutarAsync());
    });

    public void MarcarSo(Projeto projeto)
    {
        if (coordenador.EmAndamento) return;
        foreach (var outro in projetos.Todos) projetos.Marcar(outro.Caminho, outro.Caminho.Equals(projeto.Caminho, StringComparison.OrdinalIgnoreCase));
    }

    public Task DefinirOriginAsync(Projeto projeto) =>
        acoes.Proteger(() => ComSomenteAsync(projeto, () => acoes.Rodar(definirOrigin, new OpcoesDeDefinirOrigin())));

    public Task VerDiffAsync(Projeto projeto) => acoes.Proteger(async () =>
    {
        var ferramenta = FerramentasDeDiff.Normalizar(configuracoes.Atual.Git.FerramentaDeDiff);
        if (ferramenta == FerramentasDeDiff.Interno) AbrirDiffInterno(projeto);
        else if (ferramenta == FerramentasDeDiff.Tortoise) await AbrirTortoiseAsync(projeto, "repostatus");
        else await AbrirWinMergeAsync(projeto);
    });

    public Task AbrirLogNoTortoiseAsync(Projeto projeto) => acoes.Proteger(() => AbrirTortoiseAsync(projeto, "log"));

    public Task AbrirPastaAsync(Projeto projeto) => acoes.Proteger(() =>
    {
        if (!Directory.Exists(projeto.Caminho)) return dialogos.InformarAsync("Pasta não encontrada", $"{projeto.Caminho} não existe mais. Remova o projeto em Gerenciar projetos.");
        Iniciar(new ProcessStartInfo("explorer.exe", $"\"{projeto.Caminho}\"") { UseShellExecute = false });
        return Task.CompletedTask;
    });

    public Task AbrirTerminalAsync(Projeto projeto) => acoes.Proteger(() =>
    {
        var wt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps\wt.exe");
        ProcessStartInfo info;
        if (File.Exists(wt))
        {
            info = new ProcessStartInfo(wt) { UseShellExecute = false };
            info.ArgumentList.Add("-d");
            info.ArgumentList.Add(projeto.Caminho);
        }
        else
        {
            info = new ProcessStartInfo("cmd.exe") { WorkingDirectory = projeto.Caminho, UseShellExecute = false };
        }

        Iniciar(info);
        return Task.CompletedTask;
    });

    public void CopiarCaminho(Projeto projeto) => Copiar(projeto.Caminho, "Caminho copiado.");

    public Task CopiarAsync(Projeto projeto, TipoDeCopia tipo) => acoes.Proteger(async () =>
    {
        var linhas = await copia.ColetarAsync(tipo, [projeto]);
        var texto = linhas[0][(projeto.Nome.Length + 2)..];
        if (texto == "(indisponível)" || texto == "(detached HEAD)")
        {
            acoes.Avisar("Copiar", $"{projeto.Nome}: o valor não está disponível (sem origin ou detached HEAD).", false);
            return;
        }

        Copiar(texto, tipo switch
        {
            TipoDeCopia.Branches => $"Branch \"{texto}\" copiada.",
            TipoDeCopia.Hashes => $"Hash {texto[..Math.Min(9, texto.Length)]} copiado.",
            TipoDeCopia.LinksDePr => "Link do PR copiado.",
            _ => "URL do repositório copiada.",
        });
    });

    async Task ComSomenteAsync(Projeto projeto, Func<Task> acao)
    {
        var antes = projetos.Marcados.Select(p => p.Caminho).ToHashSet(StringComparer.OrdinalIgnoreCase);
        MarcarSo(projeto);
        try
        {
            await acao();
        }
        finally
        {
            foreach (var p in projetos.Todos) projetos.Marcar(p.Caminho, antes.Contains(p.Caminho));
        }
    }

    void AbrirDiffInterno(Projeto projeto)
    {
        if (!Directory.Exists(projeto.Caminho))
        {
            acoes.Avisar("Ver diff", $"{projeto.Nome}: a pasta não existe mais.", true);
            return;
        }

        Application.Current.Dispatcher.Invoke(() => diff.Criar(projeto.Caminho).Show());
    }

    async Task AbrirTortoiseAsync(Projeto projeto, string comando)
    {
        var exe = FerramentasExternasDeDiff.TortoiseProc(configuracoes.Atual.Caminhos.TortoiseGit);
        if (exe is null)
        {
            await dialogos.InformarAsync("TortoiseGit não encontrado", "Instale o TortoiseGit, informe a pasta dele em Configurações ou escolha outra ferramenta de diff em Configurações > Git.");
            return;
        }

        var argumentos = comando == "log" ? $"/command:log /path:\"{projeto.Caminho}\"" : FerramentasExternasDeDiff.ArgumentosDoTortoise(projeto.Caminho);
        Iniciar(new ProcessStartInfo(exe, argumentos) { UseShellExecute = false });
    }

    async Task AbrirWinMergeAsync(Projeto projeto)
    {
        var exe = FerramentasExternasDeDiff.WinMergeExe(configuracoes.Atual.Caminhos.WinMerge);
        if (exe is null)
        {
            await dialogos.InformarAsync("WinMerge não encontrado", "Instale o WinMerge (winmerge.org), informe o caminho dele em Configurações ou escolha outra ferramenta de diff.");
            return;
        }

        var status = (await git.ExecutarBrutoAsync(Git.Em(projeto.Caminho).Comando("status").Opcao("--porcelain"))).Linhas();
        var novos = status.Count(l => l.StartsWith("??", StringComparison.Ordinal));
        if (status.Count - novos == 0)
        {
            acoes.Avisar("Ver diff", $"{projeto.Nome}: nenhum arquivo versionado alterado.{(novos > 0 ? $" {novos} arquivo(s) novo(s) fora do git não aparecem no WinMerge." : "")}", false);
            return;
        }

        saida.Escrever($"{projeto.Nome}: abrindo o diff no WinMerge...", TipoDeLinha.Fraco);
        // o git difftool só termina quando o WinMerge fecha: roda sem segurar a tela
        _ = Task.Run(async () =>
        {
            var comando = Git.Em(projeto.Caminho).Config("difftool.menage-winmerge.cmd", FerramentasExternasDeDiff.ComandoDoWinMerge(exe))
                .Comando("difftool").Opcao("--dir-diff").Opcao("-y").Opcao("--tool=menage-winmerge").Argumento("HEAD").ComTimeout(TimeSpan.FromHours(8));
            var resultado = await git.ExecutarBrutoAsync(comando);
            if (!resultado.Sucesso) saida.Escrever($"{projeto.Nome}: o WinMerge não abriu - {resultado.TextoErro.Trim()}", TipoDeLinha.Erro);
        });
    }

    void Copiar(string texto, string aviso)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(texto));
            acoes.Avisar("Copiar", aviso, false);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            acoes.Avisar("Copiar", "A área de transferência está ocupada. Tente de novo.", true);
        }
    }

    void Iniciar(ProcessStartInfo info)
    {
        try
        {
            Process.Start(info);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            acoes.Avisar("Não foi possível abrir", ex.Message, true);
        }
    }
}
