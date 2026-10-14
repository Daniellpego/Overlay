using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Overlay
{
    // "Iniciar com o Windows" via Agendador de Tarefas: tarefa no logon do usuário com privilégio mais alto,
    // assim o overlay sobe como administrador (FPS e ping funcionando) sem pedir UAC a cada boot.
    static class InicioAutomatico
    {
        public const string NomeTarefa = "Overlay Horario Ping FPS";

        public static bool EstaAtivo()
        {
            return Schtasks("/Query /TN \"" + NomeTarefa + "\"") == 0;
        }

        // Criar tarefa com privilégio mais alto exige que o overlay esteja rodando como administrador.
        // Só a cópia instalada em Program Files pode virar tarefa: ela roda como admin sem UAC, então o exe precisa estar
        // numa pasta que só administrador altera (senão qualquer programa trocaria o exe e ganharia admin no próximo logon).
        public static bool Ativar(string caminhoExe)
        {
            if (!Pastas.Instalado)
            {
                Log.Mensagem("Iniciar com o Windows recusado: rode tools\\instalar.ps1 e use o overlay instalado em Program Files.");
                return false;
            }
            string xml = Path.Combine(Path.GetTempPath(), "overlay-tarefa-" + Path.GetRandomFileName() + ".xml");
            try
            {
                using (FileStream protegido = AbrirXmlProtegido(xml, GerarXml(caminhoExe, WindowsIdentity.GetCurrent().Name)))
                {
                    if (protegido == null) return false;
                    return Schtasks("/Create /F /TN \"" + NomeTarefa + "\" /XML \"" + xml + "\"") == 0;
                }
            }
            finally
            {
                try { File.Delete(xml); }
                catch (IOException) { }
            }
        }

        // Grava o XML, fecha e reabre SÓ para leitura negando escrita aos outros: o schtasks lê normalmente (ele recusa
        // arquivo com handle de escrita aberto), mas ninguém troca o conteúdo até o handle devolvido ser fechado.
        // Relê e compara para garantir que nada mudou entre gravar e reabrir. null = conteúdo adulterado.
        public static FileStream AbrirXmlProtegido(string caminho, string xml)
        {
            byte[] esperado;
            using (var memoria = new MemoryStream())
            {
                byte[] bom = Encoding.Unicode.GetPreamble();
                byte[] corpo = Encoding.Unicode.GetBytes(xml);
                memoria.Write(bom, 0, bom.Length);
                memoria.Write(corpo, 0, corpo.Length);
                esperado = memoria.ToArray();
            }
            using (var escrita = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                escrita.Write(esperado, 0, esperado.Length);

            var leitura = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.Read);
            var lido = new byte[esperado.Length + 1];
            int total = 0, n;
            while (total < lido.Length && (n = leitura.Read(lido, total, lido.Length - total)) > 0) total += n;
            bool igual = total == esperado.Length;
            for (int i = 0; igual && i < esperado.Length; i++) igual = lido[i] == esperado[i];
            if (igual) return leitura;

            leitura.Dispose();
            Log.Mensagem("XML da tarefa mudou entre gravar e reabrir; inicio automatico cancelado.");
            return null;
        }

        public static bool Desativar()
        {
            return Schtasks("/Delete /F /TN \"" + NomeTarefa + "\"") == 0;
        }

        // Os padrões do schtasks /SC ONLOGON matariam o overlay depois de 72 h e não o iniciariam na bateria; o XML evita isso.
        public static string GerarXml(string caminhoExe, string usuario)
        {
            string exe = SecurityElement.Escape(caminhoExe);
            string pasta = SecurityElement.Escape(Path.GetDirectoryName(caminhoExe));
            string user = SecurityElement.Escape(usuario);
            return
"<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
"  <RegistrationInfo><Description>Overlay de horario, ping e FPS.</Description></RegistrationInfo>\r\n" +
"  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT15S</Delay></LogonTrigger></Triggers>\r\n" +
"  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
"  <Settings>\r\n" +
"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\r\n" +
"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\r\n" +
"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\r\n" +
"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\r\n" +
"    <Priority>5</Priority>\r\n" + // 4–6 = normal, como pelo iniciar.bat (o padrão 7 seria abaixo do normal)
"  </Settings>\r\n" +
"  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><WorkingDirectory>" + pasta + "</WorkingDirectory></Exec></Actions>\r\n" +
"</Task>\r\n";
        }

        static int Schtasks(string argumentos)
        {
            // Caminho absoluto: "schtasks.exe" sozinho seria procurado primeiro na pasta do overlay.
            var info = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), argumentos)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(info))
            {
                string erro = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                // /Query com código != 0 só significa "tarefa não existe": não é erro.
                if (p.ExitCode != 0 && !argumentos.StartsWith("/Query"))
                    Log.Mensagem("schtasks " + argumentos + " falhou (" + p.ExitCode + "): " + erro.Trim());
                return p.ExitCode;
            }
        }
    }
}
