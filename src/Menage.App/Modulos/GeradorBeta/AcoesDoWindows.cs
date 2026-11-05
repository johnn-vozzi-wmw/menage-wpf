using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Menage.Core.Beta;
using Menage.Core.Entregas;

namespace Menage.App.Modulos.GeradorBeta;

/// <summary>Área de transferência, Explorer, bloco de notas e cmd: o que o Gerador pede ao Windows. Nada é enviado a serviço externo.</summary>
public sealed class AcoesDoWindows : IAcoesDeEntrega
{
    public bool CopiarParaAreaDeTransferencia(string arquivo)
    {
        if (!File.Exists(arquivo)) return false;
        return Application.Current.Dispatcher.Invoke(() =>
        {
            try
            {
                var dados = new DataObject();
                dados.SetFileDropList(new StringCollection { arquivo });
                dados.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
                Clipboard.SetDataObject(dados, true);
                return true;
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                return false; // outro programa segurando a área de transferência
            }
        });
    }

    public void MostrarNoExplorer(string caminho)
    {
        var argumento = File.Exists(caminho) ? $"/select,\"{caminho}\"" : $"\"{caminho}\"";
        Iniciar(new ProcessStartInfo("explorer.exe", argumento) { UseShellExecute = false });
    }

    public static void AbrirNoEditor(string arquivo) => Iniciar(new ProcessStartInfo("notepad.exe", $"\"{arquivo}\"") { UseShellExecute = false });

    /// <summary>cmd interativo na pasta do projeto, com JAVA_HOME/JAVA8_HOME/PATH do JDK do projeto.</summary>
    public static void AbrirCmd(PlanoDeProjeto plano, string java8Home, string mavenBin)
    {
        var info = new ProcessStartInfo("cmd.exe") { WorkingDirectory = plano.Pasta, UseShellExecute = false };
        info.ArgumentList.Add("/k");
        info.ArgumentList.Add($"title [{plano.Nome}] JDK {plano.Projeto.Jdk}& prompt [{plano.Nome}] $P$G");
        info.Environment["JAVA_HOME"] = plano.JavaHome;
        info.Environment["JAVA8_HOME"] = java8Home;
        info.Environment["PATH"] = $@"{plano.JavaHome}\bin;{mavenBin};{Environment.GetEnvironmentVariable("PATH")}";
        Iniciar(info);
    }

    static void Iniciar(ProcessStartInfo info)
    {
        try { Process.Start(info)?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { /* programa não encontrado: nada a fazer */ }
    }
}
